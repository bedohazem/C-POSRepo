using Microsoft.Data.Sqlite;
using System;
using POS_System.Audit;


namespace POS_System
{
    public static class StockRepo
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

        /// <summary>
        /// Adds a stock movement (+ / -). Example:
        /// PURCHASE: +qty
        /// SALE:     -qty
        /// RETURN:   +qty
        /// ADJUST:   +qty or -qty
        /// </summary>
        public static long AddMovement(
            long variantId,
            decimal qty,
            string type,
            long? refId,
            string? notes,
            int? userId,
            int? branchId)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO StockMovements(VariantId, Qty, Type, RefId, Notes, AtUtc, UserId, BranchId)
VALUES (@v,@q,@t,@r,@n,@at,@u,@b);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@v", variantId);
            cmd.Parameters.AddWithValue("@q", (double)qty);
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@r", (object?)refId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@u", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);

            var id = (long)cmd.ExecuteScalar();

            AuditLog.Write(
                "STOCK_MOVE",
                $"id={id}, variantId={variantId}, qty={qty}, type={type}, refId={(refId?.ToString() ?? "null")}, branchId={(branchId?.ToString() ?? "null")}"
            );

            return id;
        }

        public static decimal GetStock(long variantId)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT IFNULL(SUM(Qty), 0) FROM StockMovements WHERE VariantId=@v;";
            cmd.Parameters.AddWithValue("@v", variantId);

            var val = cmd.ExecuteScalar();
            return Convert.ToDecimal(val);
        }

        public static decimal GetStock(long variantId, int? branchId)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
SELECT IFNULL(SUM(Qty), 0)
FROM StockMovements
WHERE VariantId=@v
  AND (@bid IS NULL OR BranchId=@bid);";

            cmd.Parameters.AddWithValue("@v", variantId);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);

            var val = cmd.ExecuteScalar();
            return Convert.ToDecimal(val);
        }

        public static decimal GetStockForCurrentBranch(long variantId)
        {
            return GetStock(variantId, POS_System.Security.SessionManager.CurrentBranchId);
        }

    }
}
