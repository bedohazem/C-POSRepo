using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace POS_System
{
    public static class Database
    {
        public static string AppDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "POS_System");

        public static string DbPath =>
            Path.Combine(AppDataDir, "pos.db");

        public static string BackupFolder =>
            Path.Combine(AppDataDir, "Backups");

        public static string ConnStr => $"Data Source={DbPath}";

        public static void Init()
        {
            Directory.CreateDirectory(AppDataDir);
            Directory.CreateDirectory(BackupFolder);

            using var con = new SqliteConnection(ConnStr);
            con.Open();

            EnsurePragmas(con);

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Branches(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL UNIQUE,
  IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Roles(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS RolePermissions(
  RoleId INTEGER NOT NULL,
  Permission TEXT NOT NULL,
  PRIMARY KEY(RoleId, Permission),
  FOREIGN KEY(RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Users(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Username TEXT NOT NULL UNIQUE,
  PasswordHash TEXT NOT NULL,
  RoleId INTEGER NOT NULL,
  BranchId INTEGER NOT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1,
  FOREIGN KEY(RoleId) REFERENCES Roles(Id),
  FOREIGN KEY(BranchId) REFERENCES Branches(Id)
);

CREATE TABLE IF NOT EXISTS UserBranches(
  UserId INTEGER NOT NULL,
  BranchId INTEGER NOT NULL,
  PRIMARY KEY(UserId, BranchId),
  FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
  FOREIGN KEY(BranchId) REFERENCES Branches(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Products(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL,
  ImagePath TEXT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1,
  LowStockThreshold INTEGER NOT NULL DEFAULT 5
);

CREATE TABLE IF NOT EXISTS ProductVariants(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ProductId INTEGER NOT NULL,
  Barcode TEXT NOT NULL UNIQUE,
  Size TEXT NOT NULL,
  Color TEXT NOT NULL,
  SellPrice REAL NOT NULL DEFAULT 0,
  CostPrice REAL NOT NULL DEFAULT 0,
  IsActive INTEGER NOT NULL DEFAULT 1,
  FOREIGN KEY(ProductId) REFERENCES Products(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Customers(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL,
  Phone TEXT NOT NULL UNIQUE,
  Email TEXT NULL,
  Address TEXT NULL,
  Notes TEXT NULL,
  LoyaltyPoints REAL NOT NULL DEFAULT 0,
  SpecialDiscountType TEXT NOT NULL DEFAULT 'None',
  SpecialDiscountValue REAL NOT NULL DEFAULT 0,
  IsActive INTEGER NOT NULL DEFAULT 1,
  CreatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Sales(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  AtUtc TEXT NOT NULL,
  UserId INTEGER NOT NULL,
  BranchId INTEGER NOT NULL,
  CustomerId INTEGER NULL,
  SubTotal REAL NOT NULL DEFAULT 0,
  InvoiceDiscountType TEXT NOT NULL DEFAULT 'None',
  InvoiceDiscountValue REAL NOT NULL DEFAULT 0,
  GrandTotal REAL NOT NULL DEFAULT 0,
  Paid REAL NOT NULL DEFAULT 0,
  Change REAL NOT NULL DEFAULT 0,
  PaymentMethod TEXT NOT NULL DEFAULT 'Cash',
  Type TEXT NOT NULL DEFAULT 'Sale',
  RefSaleId INTEGER NULL,
  Notes TEXT NULL,
  FOREIGN KEY(UserId) REFERENCES Users(Id),
  FOREIGN KEY(BranchId) REFERENCES Branches(Id),
  FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
);

CREATE TABLE IF NOT EXISTS SaleItems(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SaleId INTEGER NOT NULL,
  VariantId INTEGER NOT NULL,
  UnitCost REAL NOT NULL DEFAULT 0,
  Name TEXT NOT NULL,
  Barcode TEXT NOT NULL,
  Size TEXT NOT NULL,
  Color TEXT NOT NULL,
  Qty INTEGER NOT NULL,
  UnitPrice REAL NOT NULL,
  LineDiscountType TEXT NOT NULL DEFAULT 'None',
  LineDiscountValue REAL NOT NULL DEFAULT 0,
  LineTotalAfterDiscount REAL NOT NULL DEFAULT 0,
  FOREIGN KEY(SaleId) REFERENCES Sales(Id) ON DELETE CASCADE,
  FOREIGN KEY(VariantId) REFERENCES ProductVariants(Id)
);

CREATE TABLE IF NOT EXISTS StockMovements(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  VariantId INTEGER NOT NULL,
  Qty REAL NOT NULL,
  Type TEXT NOT NULL,
  RefId INTEGER,
  Notes TEXT,
  AtUtc TEXT NOT NULL,
  UserId INTEGER,
  BranchId INTEGER,
  FOREIGN KEY(VariantId) REFERENCES ProductVariants(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Suppliers(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL UNIQUE,
  Phone TEXT NULL,
  Address TEXT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS SupplierPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    AtUtc TEXT NOT NULL,
    BranchId INTEGER,
    UserId INTEGER,
    Amount REAL NOT NULL,
    Notes TEXT,
    FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
);

CREATE TABLE IF NOT EXISTS Purchases(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SupplierId INTEGER NOT NULL,
  AtUtc TEXT NOT NULL,
  BranchId INTEGER NOT NULL,
  UserId INTEGER NULL,
  SubTotal REAL NOT NULL DEFAULT 0,
  Discount REAL NOT NULL DEFAULT 0,
  Total REAL NOT NULL DEFAULT 0,
  Paid REAL NOT NULL DEFAULT 0,
  Due REAL NOT NULL DEFAULT 0,
  Notes TEXT NULL,
  FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id),
  FOREIGN KEY(BranchId) REFERENCES Branches(Id),
  FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS PurchaseItems(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  PurchaseId INTEGER NOT NULL,
  VariantId INTEGER NOT NULL,
  Qty REAL NOT NULL,
  UnitCost REAL NOT NULL,
  LineTotal REAL NOT NULL,
  FOREIGN KEY(PurchaseId) REFERENCES Purchases(Id) ON DELETE CASCADE,
  FOREIGN KEY(VariantId) REFERENCES ProductVariants(Id)
);

CREATE TABLE IF NOT EXISTS LoyaltyTransactions(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CustomerId INTEGER NOT NULL,
  Type TEXT NOT NULL,
  Points REAL NOT NULL,
  RefSaleId INTEGER NULL,
  Notes TEXT NULL,
  AtUtc TEXT NOT NULL,
  UserId INTEGER NULL,
  BranchId INTEGER NULL,
  FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS AuditLogs(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  AtUtc TEXT NOT NULL,
  UserId INTEGER NULL,
  Username TEXT NOT NULL,
  BranchId INTEGER NULL,
  BranchName TEXT NULL,
  Action TEXT NOT NULL,
  EntityName TEXT NULL,
  EntityId TEXT NULL,
  Severity INTEGER NOT NULL DEFAULT 1,
  Details TEXT NULL,
  BeforeJson TEXT NULL,
  AfterJson TEXT NULL
);


CREATE TABLE IF NOT EXISTS BarcodePrintSettings(
  Id INTEGER PRIMARY KEY CHECK(Id = 1),

  LabelWidthMm REAL NOT NULL DEFAULT 50,
  LabelHeightMm REAL NOT NULL DEFAULT 35,

  NameFontSize REAL NOT NULL DEFAULT 7,
  PriceFontSize REAL NOT NULL DEFAULT 7,
  BarcodeTextFontSize REAL NOT NULL DEFAULT 6.5,

  BarcodeWidthMm REAL NOT NULL DEFAULT 39,
  BarcodeHeightMm REAL NOT NULL DEFAULT 9,

  ShowPrice INTEGER NOT NULL DEFAULT 1,
  ShowProductName INTEGER NOT NULL DEFAULT 1,
  ShowBarcodeText INTEGER NOT NULL DEFAULT 1,

  NameLeftMm REAL NOT NULL DEFAULT 1,
  NameTopMm REAL NOT NULL DEFAULT 1,

  PriceLeftMm REAL NOT NULL DEFAULT 40,
  PriceTopMm REAL NOT NULL DEFAULT 1,

  BarcodeLeftMm REAL NOT NULL DEFAULT 5.5,
  BarcodeTopMm REAL NOT NULL DEFAULT 8,

  BarcodeTextLeftMm REAL NOT NULL DEFAULT 11,
  BarcodeTextTopMm REAL NOT NULL DEFAULT 18
);

CREATE TABLE IF NOT EXISTS PrinterSettings(
  Id INTEGER PRIMARY KEY CHECK(Id = 1),
  PrinterName TEXT,
  PrintMode TEXT NOT NULL DEFAULT 'Windows' -- Windows / TSPL
);

CREATE INDEX IF NOT EXISTS IX_AuditLogs_AtUtc ON AuditLogs(AtUtc);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId_AtUtc ON AuditLogs(UserId, AtUtc);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_Entity ON AuditLogs(EntityName, EntityId, AtUtc);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_Action_AtUtc ON AuditLogs(Action, AtUtc);

CREATE INDEX IF NOT EXISTS IX_Customers_Phone ON Customers(Phone);
CREATE INDEX IF NOT EXISTS IX_Customers_Name ON Customers(Name);
CREATE INDEX IF NOT EXISTS IX_Sales_CustomerId ON Sales(CustomerId);
CREATE INDEX IF NOT EXISTS IX_Loyalty_CustomerId ON LoyaltyTransactions(CustomerId);

CREATE INDEX IF NOT EXISTS IX_Purchases_AtUtc ON Purchases(AtUtc);
CREATE INDEX IF NOT EXISTS IX_Purchases_SupplierId ON Purchases(SupplierId);
CREATE INDEX IF NOT EXISTS IX_PurchaseItems_PurchaseId ON PurchaseItems(PurchaseId);

CREATE INDEX IF NOT EXISTS IX_StockMov_VariantId ON StockMovements(VariantId);
CREATE INDEX IF NOT EXISTS IX_StockMov_Type ON StockMovements(Type);
CREATE INDEX IF NOT EXISTS IX_StockMov_AtUtc ON StockMovements(AtUtc);

CREATE INDEX IF NOT EXISTS IX_SupplierPayments_SupplierId ON SupplierPayments(SupplierId);
CREATE INDEX IF NOT EXISTS IX_SupplierPayments_AtUtc ON SupplierPayments(AtUtc);
";
            cmd.ExecuteNonQuery();

            using var seedCmd = con.CreateCommand();

            using var seedPrinter = con.CreateCommand();
            seedPrinter.CommandText = @"
                INSERT OR IGNORE INTO PrinterSettings(Id, PrinterName, PrintMode)
                VALUES (1, '', 'Windows');
                ";
            seedPrinter.ExecuteNonQuery();

            seedCmd.CommandText = @"
                INSERT OR IGNORE INTO BarcodePrintSettings(
                    Id,
                    LabelWidthMm, LabelHeightMm,
                    NameFontSize, PriceFontSize, BarcodeTextFontSize,
                    BarcodeWidthMm, BarcodeHeightMm,
                    ShowPrice, ShowProductName, ShowBarcodeText,
                    NameLeftMm, NameTopMm,
                    PriceLeftMm, PriceTopMm,
                    BarcodeLeftMm, BarcodeTopMm,
                    BarcodeTextLeftMm, BarcodeTextTopMm
                )
                VALUES
                (
                    1,
                    50, 35,
                    7, 7, 6.5,
                    39, 9,
                    1, 1, 1,
                    1, 1,
                    40, 1,
                    5.5, 8,
                    11, 18
                );";
            seedCmd.ExecuteNonQuery();

            SeedIfEmpty(con);
        }

        public static SqliteConnection Open()
        {
            var con = new SqliteConnection(ConnStr);
            con.Open();
            EnsurePragmas(con);
            return con;
        }

        private static void EnsurePragmas(SqliteConnection con)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA temp_store = MEMORY;";
            cmd.ExecuteNonQuery();
        }

        public static string CreateBackup()
        {
            Directory.CreateDirectory(BackupFolder);

            if (!File.Exists(DbPath))
                throw new FileNotFoundException("Database file not found.", DbPath);

            var fileName = $"pos_backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db";
            var backupPath = Path.Combine(BackupFolder, fileName);

            // نحاول نعمل checkpoint قبل النسخ
            try
            {
                using var con = Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // مش هنفشل الباكب بسبب checkpoint
            }

            File.Copy(DbPath, backupPath, overwrite: false);
            return backupPath;
        }

        public static void RestoreBackup(string backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentException("Backup file path is required.", nameof(backupFilePath));

            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("Backup file not found.", backupFilePath);

            Directory.CreateDirectory(AppDataDir);

            // backup احتياطي قبل الـ restore
            if (File.Exists(DbPath))
            {
                var safetyPath = Path.Combine(
                    BackupFolder,
                    $"before_restore_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");

                Directory.CreateDirectory(BackupFolder);
                File.Copy(DbPath, safetyPath, overwrite: false);
            }

            // امسح ملفات wal/shm إن وجدت
            var wal = DbPath + "-wal";
            var shm = DbPath + "-shm";

            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(shm)) File.Delete(shm);

            File.Copy(backupFilePath, DbPath, overwrite: true);
        }

        public static List<string> GetBackupFiles()
        {
            Directory.CreateDirectory(BackupFolder);

            return Directory
                .GetFiles(BackupFolder, "*.db", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }

        private static void SeedIfEmpty(SqliteConnection con)
        {
            using var check = con.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM Users;";
            var count = Convert.ToInt32(check.ExecuteScalar());
            if (count > 0) return;

            Exec(con, "INSERT INTO Branches(Name, IsActive) VALUES ('Main', 1);");
            Exec(con, "INSERT INTO Branches(Name, IsActive) VALUES ('Nasr City', 1);");

            Exec(con, "INSERT INTO Roles(Name) VALUES ('Admin');");
            Exec(con, "INSERT INTO Roles(Name) VALUES ('Cashier');");

            var adminRoleId = ScalarInt(con, "SELECT Id FROM Roles WHERE Name='Admin';");
            var cashierRoleId = ScalarInt(con, "SELECT Id FROM Roles WHERE Name='Cashier';");
            var mainBranchId = ScalarInt(con, "SELECT Id FROM Branches WHERE Name='Main';");
            var nasrBranchId = ScalarInt(con, "SELECT Id FROM Branches WHERE Name='Nasr City';");

            string[] perms = new[]
            {
                "Sale_Create","Return_Create","Discount_Apply","Reports_View",
                "Settings_Edit","Drawer_Open","Print_Receipt","Users_Manage"
            };

            foreach (var p in perms)
                Exec(con, "INSERT INTO RolePermissions(RoleId, Permission) VALUES (@rid, @p);",
                    ("@rid", adminRoleId), ("@p", p));

            string[] cashierPerms = new[] { "Sale_Create", "Return_Create", "Print_Receipt" };
            foreach (var p in cashierPerms)
                Exec(con, "INSERT INTO RolePermissions(RoleId, Permission) VALUES (@rid, @p);",
                    ("@rid", cashierRoleId), ("@p", p));

            var adminHash = Security.AuthService.Hash("admin123");
            Exec(con, @"INSERT INTO Users(Username,PasswordHash,RoleId,BranchId,IsActive)
                        VALUES (@u,@h,@r,@b,1);",
                ("@u", "admin"), ("@h", adminHash), ("@r", adminRoleId), ("@b", mainBranchId));

            var cashierHash = Security.AuthService.Hash("1234");
            Exec(con, @"INSERT INTO Users(Username,PasswordHash,RoleId,BranchId,IsActive)
                        VALUES (@u,@h,@r,@b,1);",
                ("@u", "cashier"), ("@h", cashierHash), ("@r", cashierRoleId), ("@b", nasrBranchId));

            Exec(con, "INSERT INTO UserBranches(UserId, BranchId) SELECT Id, BranchId FROM Users;");
        }

        private static void Exec(SqliteConnection con, string sql, params (string k, object v)[] ps)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
            cmd.ExecuteNonQuery();
        }

        private static int ScalarInt(SqliteConnection con, string sql)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}