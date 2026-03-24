using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace POS_System
{
    public class ProductRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? ImagePath { get; set; }
        public int LowStockThreshold { get; set; }
        public bool IsActive { get; set; }
    }

    public class VariantRow
    {
        public long Id { get; set; }
        public long ProductId { get; set; }

        public string ProductName { get; set; } = "";
        public int LowStockThreshold { get; set; }

        public string Barcode { get; set; } = "";
        public string Size { get; set; } = "";
        public string Color { get; set; } = "";

        public decimal SellPrice { get; set; }
        public decimal CostPrice { get; set; }

        public bool IsActive { get; set; }

        // محسوب من StockMovements
        public decimal Stock { get; set; }

        // ✅ تنبيه قرب النفاد
        public bool IsLowStock => Stock <= LowStockThreshold;
    }

    public static class ProductRepo
    {
        private static SqliteConnection Open()
        {
            var con = new SqliteConnection(Database.ConnStr);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
            return con;
        }

        // =========================
        // ===== Products ==========
        // =========================

        public static long CreateProduct(string name, string? imagePath, int lowStockThreshold, bool isActive = true)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Products(Name, ImagePath, LowStockThreshold, IsActive)
VALUES (@n,@img,@th,@a);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@img", (object?)imagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@th", lowStockThreshold < 0 ? 0 : lowStockThreshold);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);

            var newId = (long)cmd.ExecuteScalar();

            POS_System.Audit.AuditLog.Write("PRODUCT_CREATE", $"id={newId}, name={name}");

            return newId;
        }

        public static void UpdateProduct(long id, string name, string? imagePath, int lowStockThreshold, bool isActive)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE Products
SET Name=@n, ImagePath=@img, LowStockThreshold=@th, IsActive=@a
WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@img", (object?)imagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@th", lowStockThreshold < 0 ? 0 : lowStockThreshold);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
            cmd.ExecuteNonQuery();

            POS_System.Audit.AuditLog.Write("PRODUCT_UPDATE", $"id={id}, name={name}");
        }

        public static List<ProductRow> GetProducts(string? search = null, bool includeInactive = true)
        {
            var list = new List<ProductRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();

            if (string.IsNullOrWhiteSpace(search))
            {
                cmd.CommandText = includeInactive
                    ? "SELECT Id, Name, ImagePath, LowStockThreshold, IsActive FROM Products ORDER BY Id DESC;"
                    : "SELECT Id, Name, ImagePath, LowStockThreshold, IsActive FROM Products WHERE IsActive=1 ORDER BY Id DESC;";
            }
            else
            {
                cmd.CommandText = includeInactive
                    ? "SELECT Id, Name, ImagePath, LowStockThreshold, IsActive FROM Products WHERE Name LIKE @q ORDER BY Id DESC;"
                    : "SELECT Id, Name, ImagePath, LowStockThreshold, IsActive FROM Products WHERE IsActive=1 AND Name LIKE @q ORDER BY Id DESC;";
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
            }

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new ProductRow
                {
                    Id = rd.GetInt64(0),
                    Name = rd.GetString(1),
                    ImagePath = rd.IsDBNull(2) ? null : rd.GetString(2),
                    LowStockThreshold = rd.IsDBNull(3) ? 5 : rd.GetInt32(3),
                    IsActive = rd.GetInt32(4) == 1
                });
            }

            return list;
        }

        public static void ToggleProductActive(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Products SET IsActive = CASE IsActive WHEN 1 THEN 0 ELSE 1 END WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            POS_System.Audit.AuditLog.Write("PRODUCT_TOGGLE_ACTIVE", $"id={id}");
        }

        public static void DeleteProduct(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Products WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            POS_System.Audit.AuditLog.Write("PRODUCT_DELETE", $"id={id}");
        }

        // =========================
        // ===== Variants ==========
        // =========================

        public static List<VariantRow> GetVariantsByProduct(long productId, bool includeInactive = true)
        {
            var list = new List<VariantRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();

            var activeFilter = includeInactive ? "" : "AND v.IsActive=1 AND p.IsActive=1";

            cmd.CommandText = $@"
SELECT
  v.Id, v.ProductId,
  p.Name AS ProductName,
  IFNULL(p.LowStockThreshold, 5) AS Threshold,
  v.Barcode, v.Size, v.Color, v.SellPrice, v.CostPrice, v.IsActive,
  IFNULL((
    SELECT SUM(sm.Qty)
    FROM StockMovements sm
    WHERE sm.VariantId = v.Id
      AND (@bid IS NULL OR sm.BranchId = @bid)
  ), 0) AS Stock
FROM ProductVariants v
JOIN Products p ON p.Id = v.ProductId
WHERE v.ProductId=@pid
{activeFilter}
ORDER BY v.Id DESC;";

            cmd.Parameters.AddWithValue("@pid", productId);
            cmd.Parameters.AddWithValue("@bid", (object?)POS_System.Security.SessionManager.CurrentBranchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new VariantRow
                {
                    Id = rd.GetInt64(0),
                    ProductId = rd.GetInt64(1),
                    ProductName = rd.GetString(2),
                    LowStockThreshold = rd.GetInt32(3),
                    Barcode = rd.GetString(4),
                    Size = rd.GetString(5),
                    Color = rd.GetString(6),
                    SellPrice = Convert.ToDecimal(rd.GetDouble(7)),
                    CostPrice = Convert.ToDecimal(rd.GetDouble(8)),
                    IsActive = rd.GetInt32(9) == 1,
                    Stock = Convert.ToDecimal(rd.GetDouble(10))
                });
            }

            return list;
        }
        public static List<VariantRow> SearchVariantsByBarcodeOrName(string query, int limit = 50, bool includeInactive = true)
        {
            var list = new List<VariantRow>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            using var con = Open();
            using var cmd = con.CreateCommand();

            var activeFilter = includeInactive ? "" : "AND v.IsActive=1 AND p.IsActive=1";

            cmd.CommandText = $@"
SELECT
  v.Id, v.ProductId,
  p.Name AS ProductName,
  IFNULL(p.LowStockThreshold, 5) AS Threshold,
  v.Barcode, v.Size, v.Color, v.SellPrice, v.CostPrice, v.IsActive,
  IFNULL((
    SELECT SUM(sm.Qty)
    FROM StockMovements sm
    WHERE sm.VariantId = v.Id
      AND (@bid IS NULL OR sm.BranchId = @bid)
  ), 0) AS Stock
FROM ProductVariants v
JOIN Products p ON p.Id = v.ProductId
WHERE 1=1
{activeFilter}
AND (
  v.Barcode = @qExact
  OR v.Barcode LIKE @qLike
  OR p.Name LIKE @qLike
)
ORDER BY
  CASE WHEN v.Barcode = @qExact THEN 0 ELSE 1 END,
  p.Name
LIMIT @lim;";

            var q = query.Trim();
            cmd.Parameters.AddWithValue("@qExact", q);
            cmd.Parameters.AddWithValue("@qLike", "%" + q + "%");
            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.Parameters.AddWithValue("@bid", (object?)POS_System.Security.SessionManager.CurrentBranchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new VariantRow
                {
                    Id = rd.GetInt64(0),
                    ProductId = rd.GetInt64(1),
                    ProductName = rd.GetString(2),
                    LowStockThreshold = rd.GetInt32(3),
                    Barcode = rd.GetString(4),
                    Size = rd.GetString(5),
                    Color = rd.GetString(6),
                    SellPrice = Convert.ToDecimal(rd.GetDouble(7)),
                    CostPrice = Convert.ToDecimal(rd.GetDouble(8)),
                    IsActive = rd.GetInt32(9) == 1,
                    Stock = Convert.ToDecimal(rd.GetDouble(10))
                });
            }
            return list;
        }

        public static VariantRow? GetVariantByBarcode(string barcode)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
  v.Id, v.ProductId,
  p.Name AS ProductName,
  IFNULL(p.LowStockThreshold, 5) AS Threshold,
  v.Barcode, v.Size, v.Color, v.SellPrice, v.CostPrice, v.IsActive,
  IFNULL((
    SELECT SUM(sm.Qty)
    FROM StockMovements sm
    WHERE sm.VariantId = v.Id
      AND (@bid IS NULL OR sm.BranchId = @bid)
  ), 0) AS Stock
FROM ProductVariants v
JOIN Products p ON p.Id = v.ProductId
WHERE v.Barcode=@bc
LIMIT 1;";

            cmd.Parameters.AddWithValue("@bc", barcode.Trim());
            cmd.Parameters.AddWithValue("@bid", (object?)POS_System.Security.SessionManager.CurrentBranchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return new VariantRow
            {
                Id = rd.GetInt64(0),
                ProductId = rd.GetInt64(1),
                ProductName = rd.GetString(2),
                LowStockThreshold = rd.GetInt32(3),
                Barcode = rd.GetString(4),
                Size = rd.GetString(5),
                Color = rd.GetString(6),
                SellPrice = Convert.ToDecimal(rd.GetDouble(7)),
                CostPrice = Convert.ToDecimal(rd.GetDouble(8)),
                IsActive = rd.GetInt32(9) == 1,
                Stock = Convert.ToDecimal(rd.GetDouble(10))
            };
        }
        public static void ToggleVariantActive(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE ProductVariants SET IsActive = CASE IsActive WHEN 1 THEN 0 ELSE 1 END WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            POS_System.Audit.AuditLog.Write("Variant_TOGGLE_ACTIVE", $"id={id}");

        }

        public static void DeleteVariant(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM ProductVariants WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            POS_System.Audit.AuditLog.Write("VARIANT_TOGGLE_ACTIVE", $"id={id}");
        }

        public static long CreateVariant(long productId, string barcode, string size, string color,
            decimal sellPrice, decimal costPrice, bool isActive = true)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO ProductVariants(ProductId, Barcode, Size, Color, SellPrice, CostPrice, IsActive)
VALUES (@pid,@bc,@sz,@cl,@sp,@cp,@a);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@pid", productId);
            cmd.Parameters.AddWithValue("@bc", barcode.Trim());
            cmd.Parameters.AddWithValue("@sz", size.Trim());
            cmd.Parameters.AddWithValue("@cl", color.Trim());
            cmd.Parameters.AddWithValue("@sp", (double)Math.Round(sellPrice, 2));
            cmd.Parameters.AddWithValue("@cp", (double)Math.Round(costPrice, 2));
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
            var newId = (long)cmd.ExecuteScalar();
            POS_System.Audit.AuditLog.Write("VARIANT_CREATE", $"id={newId}, barcode={barcode}");
            return newId;

        }

        public static void UpdateVariant(long id, string barcode, string size, string color,
            decimal sellPrice, decimal costPrice, bool isActive)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE ProductVariants
SET Barcode=@bc, Size=@sz, Color=@cl, SellPrice=@sp, CostPrice=@cp, IsActive=@a
WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@bc", barcode.Trim());
            cmd.Parameters.AddWithValue("@sz", size.Trim());
            cmd.Parameters.AddWithValue("@cl", color.Trim());
            cmd.Parameters.AddWithValue("@sp", (double)Math.Round(sellPrice, 2));
            cmd.Parameters.AddWithValue("@cp", (double)Math.Round(costPrice, 2));
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
            cmd.ExecuteNonQuery();
            POS_System.Audit.AuditLog.Write("VARIANT_UPDATE", $"id={id}, barcode={barcode}");
        }
        public static List<VariantRow> GetAllVariants(int limit = 200, bool includeInactive = true)
        {
            var list = new List<VariantRow>();

            using var con = Open();
            using var cmd = con.CreateCommand();

            var activeFilter = includeInactive ? "" : "AND v.IsActive=1 AND p.IsActive=1";

            cmd.CommandText = $@"
SELECT
  v.Id, v.ProductId,
  p.Name AS ProductName,
  IFNULL(p.LowStockThreshold, 5) AS Threshold,
  v.Barcode, v.Size, v.Color, v.SellPrice, v.CostPrice, v.IsActive,
  IFNULL((
    SELECT SUM(sm.Qty)
    FROM StockMovements sm
    WHERE sm.VariantId = v.Id
      AND (@bid IS NULL OR sm.BranchId = @bid)
  ), 0) AS Stock
FROM ProductVariants v
JOIN Products p ON p.Id = v.ProductId
WHERE 1=1
{activeFilter}
ORDER BY p.Name, v.Barcode
LIMIT @lim;";

            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.Parameters.AddWithValue("@bid", (object?)POS_System.Security.SessionManager.CurrentBranchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new VariantRow
                {
                    Id = rd.GetInt64(0),
                    ProductId = rd.GetInt64(1),
                    ProductName = rd.GetString(2),
                    LowStockThreshold = rd.GetInt32(3),
                    Barcode = rd.GetString(4),
                    Size = rd.GetString(5),
                    Color = rd.GetString(6),
                    SellPrice = Convert.ToDecimal(rd.GetDouble(7)),
                    CostPrice = Convert.ToDecimal(rd.GetDouble(8)),
                    IsActive = rd.GetInt32(9) == 1,
                    Stock = Convert.ToDecimal(rd.GetDouble(10))
                });
            }

            return list;
        }
        public class InventoryRow
        {
            public long VariantId { get; set; }
            public long ProductId { get; set; }
            public string ProductName { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Size { get; set; } = "";
            public string Color { get; set; } = "";
            public decimal SellPrice { get; set; }
            public decimal CostPrice { get; set; }
            public bool IsActive { get; set; }
            public decimal Stock { get; set; }

            // ✅ جديد (عشان تنبيه المخزون في صفحة المخزن كمان)
            public int LowStockThreshold { get; set; }
            public bool IsLowStock => Stock <= LowStockThreshold;
        }

        public static List<InventoryRow> GetInventory(string? search = null, bool includeInactive = true)
        {
            var list = new List<InventoryRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();

            var where = "";
            if (!string.IsNullOrWhiteSpace(search))
            {
                where = @"
AND (
    p.Name LIKE @q OR
    v.Barcode LIKE @q OR
    v.Size LIKE @q OR
    v.Color LIKE @q
)";
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
            }

            var activeFilter = includeInactive ? "" : "AND v.IsActive=1 AND p.IsActive=1";

            cmd.CommandText = $@"
SELECT
  v.Id AS VariantId,
  v.ProductId,
  p.Name AS ProductName,
  v.Barcode,
  v.Size,
  v.Color,
  v.SellPrice,
  v.CostPrice,
  v.IsActive,
  IFNULL((
  SELECT SUM(sm.Qty)
  FROM StockMovements sm
  WHERE sm.VariantId = v.Id
    AND (@bid IS NULL OR sm.BranchId = @bid)
), 0) AS Stock,
  IFNULL(p.LowStockThreshold, 5) AS Threshold
FROM ProductVariants v
JOIN Products p ON p.Id = v.ProductId
WHERE 1=1
{activeFilter}
{where}
ORDER BY p.Name, v.Id DESC
LIMIT 500;";

            cmd.Parameters.AddWithValue("@bid", (object?)POS_System.Security.SessionManager.CurrentBranchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new InventoryRow
                {
                    VariantId = rd.GetInt64(0),
                    ProductId = rd.GetInt64(1),
                    ProductName = rd.GetString(2),
                    Barcode = rd.GetString(3),
                    Size = rd.GetString(4),
                    Color = rd.GetString(5),
                    SellPrice = Convert.ToDecimal(rd.GetDouble(6)),
                    CostPrice = Convert.ToDecimal(rd.GetDouble(7)),
                    IsActive = rd.GetInt32(8) == 1,
                    Stock = Convert.ToDecimal(rd.GetDouble(9)),
                    LowStockThreshold = rd.GetInt32(10)
                });
            }

            return list;
        }

      


    }
}
