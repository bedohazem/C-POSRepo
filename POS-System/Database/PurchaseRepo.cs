using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using POS_System.Audit;

namespace POS_System
{
    public class SupplierRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }

    }


    public class PurchaseItemInput
    {
        public long VariantId { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitCost { get; set; }
    }
    public class PurchaseRow
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public string AtUtc { get; set; } = "";
        public int BranchId { get; set; }
        public int? UserId { get; set; }

        public decimal SubTotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Due { get; set; }

        public string? Notes { get; set; }
    }

    public class PurchaseItemRow
    {
        public long Id { get; set; }
        public long PurchaseId { get; set; }
        public long VariantId { get; set; }

        public string ProductName { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string Size { get; set; } = "";
        public string Color { get; set; } = "";

        public decimal Qty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class SupplierPaymentRow
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public string AtUtc { get; set; } = "";
        public int? BranchId { get; set; }
        public int? UserId { get; set; }
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
    }
    public static class PurchaseRepo
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

        // ===== Suppliers =====
        public static List<SupplierRow> GetSuppliers(string? search = null, bool includeInactive = true)
        {
            var list = new List<SupplierRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();

            var where = "";
            if (!string.IsNullOrWhiteSpace(search))
            {
                where = " AND (Name LIKE @q OR Phone LIKE @q)";
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
            }

            var active = includeInactive ? "" : " AND IsActive=1";

            cmd.CommandText = $@"
SELECT Id, Name, Phone, Address, IsActive
FROM Suppliers
WHERE 1=1 {active} {where}
ORDER BY Name
LIMIT 500;";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new SupplierRow
                {
                    Id = rd.GetInt64(0),
                    Name = rd.IsDBNull(1) ? "بدون اسم" : rd.GetString(1),
                    Phone = rd.IsDBNull(2) ? null : rd.GetString(2),
                    Address = rd.IsDBNull(3) ? null : rd.GetString(3),
                    IsActive = rd.GetInt32(4) == 1
                });
            }
            return list;
        }

        public static long CreateSupplier(string name, string? phone, string? address, bool isActive = true)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Suppliers(Name, Phone, Address, IsActive)
VALUES (@n,@p,@a,@x);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@p", (object?)phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@a", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@x", isActive ? 1 : 0);

            var id = (long)cmd.ExecuteScalar();

            AuditLog.Write("SUPPLIER_CREATE", $"id={id}, name={name}");

            return id;
        }

        // ===== Purchases (Header + Items + StockMovements) =====
        public static long CreatePurchase(
            long supplierId,
            int branchId,
            int? userId,
            decimal discount,
            decimal paid,
            string? notes,
            List<PurchaseItemInput> items)
        {
            if (items == null || items.Count == 0)
                throw new Exception("Purchase items required.");

            using var con = Open();
            using var tx = con.BeginTransaction();

            decimal subTotal = 0m;
            foreach (var it in items)
            {
                if (it.Qty <= 0) throw new Exception("Qty must be > 0.");
                if (it.UnitCost < 0) throw new Exception("Unit cost invalid.");
                subTotal += it.Qty * it.UnitCost;
            }

            var total = subTotal - discount;
            if (total < 0) total = 0;
            if (paid < 0)
                throw new Exception("Paid cannot be negative.");

            if (paid > total)
                throw new Exception("Paid cannot exceed total.");
            var due = total - paid;
            if (due < 0) due = 0;

            long purchaseId;
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO Purchases(SupplierId, AtUtc, BranchId, UserId, SubTotal, Discount, Total, Paid, Due, Notes)
VALUES (@sid,@at,@bid,@uid,@sub,@disc,@tot,@paid,@due,@notes);
SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@sid", supplierId);
                cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@bid", branchId);
                cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sub", (double)subTotal);
                cmd.Parameters.AddWithValue("@disc", (double)discount);
                cmd.Parameters.AddWithValue("@tot", (double)total);
                cmd.Parameters.AddWithValue("@paid", (double)paid);
                cmd.Parameters.AddWithValue("@due", (double)due);
                cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);

                purchaseId = (long)cmd.ExecuteScalar();
            }

            // Insert items + add stock movements
            foreach (var it in items)
            {
                var lineTotal = it.Qty * it.UnitCost;

                using (var cmd = con.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO PurchaseItems(PurchaseId, VariantId, Qty, UnitCost, LineTotal)
VALUES (@pid,@vid,@q,@uc,@lt);";
                    cmd.Parameters.AddWithValue("@pid", purchaseId);
                    cmd.Parameters.AddWithValue("@vid", it.VariantId);
                    cmd.Parameters.AddWithValue("@q", (double)it.Qty);
                    cmd.Parameters.AddWithValue("@uc", (double)it.UnitCost);
                    cmd.Parameters.AddWithValue("@lt", (double)lineTotal);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = con.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO StockMovements(VariantId, Qty, Type, RefId, Notes, AtUtc, UserId, BranchId)
VALUES (@v,@q,'PURCHASE',@ref,@n,@at,@u,@b);";
                    cmd.Parameters.AddWithValue("@v", it.VariantId);
                    cmd.Parameters.AddWithValue("@q", (double)it.Qty); // +qty
                    cmd.Parameters.AddWithValue("@ref", purchaseId);
                    cmd.Parameters.AddWithValue("@n", (object?)notes ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@u", (object?)userId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@b", branchId);
                    cmd.ExecuteNonQuery();
                }
            }

            tx.Commit();

            AuditLog.Write(
                "PURCHASE_CREATE",
                $"purchaseId={purchaseId}, supplierId={supplierId}, branchId={branchId}, userId={(userId?.ToString() ?? "null")}, items={items.Count}, subTotal={subTotal}, discount={discount}, total={total}, paid={paid}, due={due}"
            );

            return purchaseId;
        }

        public static List<PurchaseRow> GetPurchases(
    string? search = null,
    int? branchId = null,
    int limit = 300)
        {
            var list = new List<PurchaseRow>();

            using var con = Open();
            using var cmd = con.CreateCommand();

            var where = @"
WHERE (@bid IS NULL OR p.BranchId = @bid)";

            if (!string.IsNullOrWhiteSpace(search))
            {
                where += @"
AND (
    s.Name LIKE @q
    OR CAST(p.Id AS TEXT) LIKE @q
    OR IFNULL(p.Notes, '') LIKE @q
)";
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
            }

            cmd.CommandText = $@"
SELECT
    p.Id,
    p.SupplierId,
    s.Name,
    p.AtUtc,
    p.BranchId,
    p.UserId,
    p.SubTotal,
    p.Discount,
    p.Total,
    p.Paid,
    p.Due,
    p.Notes
FROM Purchases p
JOIN Suppliers s ON s.Id = p.SupplierId
{where}
ORDER BY p.Id DESC
LIMIT @lim;";

            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lim", limit);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new PurchaseRow
                {
                    Id = rd.GetInt64(0),
                    SupplierId = rd.GetInt64(1),
                    SupplierName = rd.GetString(2),
                    AtUtc = rd.GetString(3),
                    BranchId = rd.GetInt32(4),
                    UserId = rd.IsDBNull(5) ? null : rd.GetInt32(5),
                    SubTotal = Convert.ToDecimal(rd.GetDouble(6)),
                    Discount = Convert.ToDecimal(rd.GetDouble(7)),
                    Total = Convert.ToDecimal(rd.GetDouble(8)),
                    Paid = Convert.ToDecimal(rd.GetDouble(9)),
                    Due = Convert.ToDecimal(rd.GetDouble(10)),
                    Notes = rd.IsDBNull(11) ? null : rd.GetString(11)
                });
            }

            return list;
        }

        public static long AddSupplierPayment(
            long supplierId,
            int? branchId,
            int? userId,
            decimal amount,
            string? notes)
        {
            if (amount <= 0)
                throw new Exception("Amount must be greater than zero.");

            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
INSERT INTO SupplierPayments(SupplierId, AtUtc, BranchId, UserId, Amount, Notes)
VALUES (@sid, @at, @bid, @uid, @amt, @notes);
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@sid", supplierId);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@amt", (double)amount);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);

            var id = (long)cmd.ExecuteScalar();

            AuditLog.Write(
                "SUPPLIER_PAYMENT_ADD",
                $"paymentId={id}, supplierId={supplierId}, branchId={(branchId?.ToString() ?? "null")}, userId={(userId?.ToString() ?? "null")}, amount={amount}"
            );

            return id;
        }

        public static List<SupplierPaymentRow> GetSupplierPayments(long supplierId, int? branchId)
        {
            var list = new List<SupplierPaymentRow>();

            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
SELECT
    sp.Id,
    sp.SupplierId,
    s.Name,
    sp.AtUtc,
    sp.BranchId,
    sp.UserId,
    sp.Amount,
    sp.Notes
FROM SupplierPayments sp
JOIN Suppliers s ON s.Id = sp.SupplierId
WHERE sp.SupplierId = @sid
  AND (@bid IS NULL OR sp.BranchId = @bid)
ORDER BY sp.Id DESC;";

            cmd.Parameters.AddWithValue("@sid", supplierId);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new SupplierPaymentRow
                {
                    Id = rd.GetInt64(0),
                    SupplierId = rd.GetInt64(1),
                    SupplierName = rd.GetString(2),
                    AtUtc = rd.GetString(3),
                    BranchId = rd.IsDBNull(4) ? null : rd.GetInt32(4),
                    UserId = rd.IsDBNull(5) ? null : rd.GetInt32(5),
                    Amount = Convert.ToDecimal(rd.GetDouble(6)),
                    Notes = rd.IsDBNull(7) ? null : rd.GetString(7)
                });
            }

            return list;
        }
        public static List<PurchaseItemRow> GetPurchaseItems(long purchaseId)
        {
            var list = new List<PurchaseItemRow>();

            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
SELECT
    pi.Id,
    pi.PurchaseId,
    pi.VariantId,
    p.Name,
    v.Barcode,
    v.Size,
    v.Color,
    pi.Qty,
    pi.UnitCost,
    pi.LineTotal
FROM PurchaseItems pi
JOIN ProductVariants v ON v.Id = pi.VariantId
JOIN Products p ON p.Id = v.ProductId
WHERE pi.PurchaseId = @pid
ORDER BY pi.Id;";

            cmd.Parameters.AddWithValue("@pid", purchaseId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new PurchaseItemRow
                {
                    Id = rd.GetInt64(0),
                    PurchaseId = rd.GetInt64(1),
                    VariantId = rd.GetInt64(2),
                    ProductName = rd.GetString(3),
                    Barcode = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    Size = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Color = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    Qty = Convert.ToDecimal(rd.GetDouble(7)),
                    UnitCost = Convert.ToDecimal(rd.GetDouble(8)),
                    LineTotal = Convert.ToDecimal(rd.GetDouble(9))
                });
            }

            return list;
        }
        public class SupplierStatementRow
        {
            public long PurchaseId { get; set; }
            public string AtUtc { get; set; } = "";

            public decimal Total { get; set; }
            public decimal Paid { get; set; }
            public decimal Due { get; set; }

            public string? Notes { get; set; }
        }

        public static (List<SupplierStatementRow> rows, decimal purchasesTotal, decimal invoicePaid, decimal extraPayments, decimal due)
        GetSupplierStatement(long supplierId, int? branchId)
        {
            var list = new List<SupplierStatementRow>();

            using var con = Open();

            decimal purchasesTotal = 0m;
            decimal invoicePaid = 0m;
            decimal purchasesDue = 0m;
            decimal extraPayments = 0m;

            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    Id,
    AtUtc,
    Total,
    Paid,
    Due,
    Notes
FROM Purchases
WHERE SupplierId = @sid
  AND (@bid IS NULL OR BranchId = @bid)
ORDER BY Id DESC;";

                cmd.Parameters.AddWithValue("@sid", supplierId);
                cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var row = new SupplierStatementRow
                    {
                        PurchaseId = rd.GetInt64(0),
                        AtUtc = rd.GetString(1),
                        Total = Convert.ToDecimal(rd.GetDouble(2)),
                        Paid = Convert.ToDecimal(rd.GetDouble(3)),
                        Due = Convert.ToDecimal(rd.GetDouble(4)),
                        Notes = rd.IsDBNull(5) ? null : rd.GetString(5)
                    };

                    purchasesTotal += row.Total;
                    invoicePaid += row.Paid;
                    purchasesDue += row.Due;

                    list.Add(row);
                }
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
SELECT IFNULL(SUM(Amount), 0)
FROM SupplierPayments
WHERE SupplierId = @sid
  AND (@bid IS NULL OR BranchId = @bid);";

                cmd.Parameters.AddWithValue("@sid", supplierId);
                cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

                extraPayments = Convert.ToDecimal(cmd.ExecuteScalar());
            }

            var finalDue = purchasesDue - extraPayments;
            if (finalDue < 0) finalDue = 0;

            return (list, purchasesTotal, invoicePaid, extraPayments, finalDue);
        }
    }
}