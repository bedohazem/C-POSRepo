using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using POS_System.Audit;

namespace POS_System
{
    public class LoyaltyTxRow
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
        public string Type { get; set; } = "EARN";
        public decimal Points { get; set; }
        public long? RefSaleId { get; set; }
        public string? Notes { get; set; }
        public string AtUtc { get; set; } = "";
    }

    // ✅ renamed (was CustomerSaleRow)
    public class CustomerSaleMiniRow
    {
        public long SaleId { get; set; }
        public string AtUtc { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public decimal GrandTotal { get; set; }
        public string Type { get; set; } = "Sale";
    }

    public static class LoyaltyRepo
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

        public static long AddTx(long customerId, string type, decimal points, long? refSaleId, string? notes, int? userId, int? branchId)
        {

                    using var con = Open();
                    using var tx = con.BeginTransaction();

                    long id;
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
INSERT INTO LoyaltyTransactions(CustomerId, Type, Points, RefSaleId, Notes, AtUtc, UserId, BranchId)
VALUES (@c,@t,@p,@r,@n,@at,@u,@b);
SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@c", customerId);
                        cmd.Parameters.AddWithValue("@t", type);
                        cmd.Parameters.AddWithValue("@p", (double)points);
                        cmd.Parameters.AddWithValue("@r", (object?)refSaleId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@n", (object?)notes ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@u", (object?)userId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);

                        id = (long)cmd.ExecuteScalar();
                    }

                    // Recalc balance
                    decimal balance;
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT IFNULL(SUM(Points), 0) FROM LoyaltyTransactions WHERE CustomerId=@c;";
                        cmd.Parameters.AddWithValue("@c", customerId);
                        balance = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE Customers SET LoyaltyPoints=@p WHERE Id=@id;";
                        cmd.Parameters.AddWithValue("@id", customerId);
                        cmd.Parameters.AddWithValue("@p", (double)balance);
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
            AuditLog.Write("LOYALTY_TX_ADD", $"customerId={customerId}, type={type}, points={points}, refSaleId={(refSaleId?.ToString() ?? "null")}");
            
            return id;
                
        }

        public static List<LoyaltyTxRow> GetTransactions(long customerId, int limit = 200)
        {
            var list = new List<LoyaltyTxRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT Id, CustomerId, Type, Points, RefSaleId, Notes, AtUtc
FROM LoyaltyTransactions
WHERE CustomerId=@c
ORDER BY Id DESC
LIMIT {limit};";
            cmd.Parameters.AddWithValue("@c", customerId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new LoyaltyTxRow
                {
                    Id = rd.GetInt64(0),
                    CustomerId = rd.GetInt64(1),
                    Type = rd.GetString(2),
                    Points = Convert.ToDecimal(rd.GetDouble(3)),
                    RefSaleId = rd.IsDBNull(4) ? null : rd.GetInt64(4),
                    Notes = rd.IsDBNull(5) ? null : rd.GetString(5),
                    AtUtc = rd.GetString(6)
                });
            }
            return list;
        }

        public static List<CustomerSaleMiniRow> GetCustomerSales(long customerId, int limit = 200)
        {
            var list = new List<CustomerSaleMiniRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT Id, AtUtc, PaymentMethod, GrandTotal, Type
FROM Sales
WHERE CustomerId=@c
ORDER BY Id DESC
LIMIT {limit};";
            cmd.Parameters.AddWithValue("@c", customerId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new CustomerSaleMiniRow
                {
                    SaleId = rd.GetInt64(0),
                    AtUtc = rd.GetString(1),
                    PaymentMethod = rd.GetString(2),
                    GrandTotal = Convert.ToDecimal(rd.GetDouble(3)),
                    Type = rd.GetString(4)
                });
            }
            return list;
        }
    }
}