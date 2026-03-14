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
                    Name = rd.GetString(1),
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
    }
}