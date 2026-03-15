using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace POS_System
{
    public static class CustomerRepo
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

        public static List<CustomerRow> Search(string? q, int limit = 200, bool activeOnly = true)
        {
            var list = new List<CustomerRow>();
            q = (q ?? "").Trim();

            using var con = Open();
            using var cmd = con.CreateCommand();

            var safeLimit = Math.Max(1, limit);
            var where = activeOnly ? "WHERE IsActive=1" : "WHERE 1=1";

            if (string.IsNullOrWhiteSpace(q))
            {
                cmd.CommandText = $@"
SELECT Id, Name, Phone, Email, Address, Notes,
       LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue,
       IsActive, CreatedAtUtc
FROM Customers
{where}
ORDER BY Id DESC
LIMIT {safeLimit};";
            }
            else
            {
                cmd.CommandText = $@"
SELECT Id, Name, Phone, Email, Address, Notes,
       LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue,
       IsActive, CreatedAtUtc
FROM Customers
{where}
  AND (Name LIKE @q OR Phone LIKE @q)
ORDER BY Id DESC
LIMIT {safeLimit};";

                cmd.Parameters.AddWithValue("@q", "%" + q + "%");
            }

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new CustomerRow
                {
                    Id = rd.GetInt64(0),
                    Name = rd.GetString(1),
                    Phone = rd.GetString(2),
                    Email = rd.IsDBNull(3) ? null : rd.GetString(3),
                    Address = rd.IsDBNull(4) ? null : rd.GetString(4),
                    Notes = rd.IsDBNull(5) ? null : rd.GetString(5),
                    LoyaltyPoints = ReadDecimal(rd, 6),
                    SpecialDiscountType = rd.IsDBNull(7) ? "None" : rd.GetString(7),
                    SpecialDiscountValue = ReadDecimal(rd, 8),
                    IsActive = rd.GetInt32(9) == 1,
                    CreatedAtUtc = rd.IsDBNull(10) ? "" : rd.GetString(10),
                });
            }

            return list;
        }

        public static long Add(
            string name,
            string phone,
            string? email,
            string? address,
            string? notes,
            string specialDiscountType,
            decimal specialDiscountValue,
            bool isActive = true)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
INSERT INTO Customers
(Name, Phone, Email, Address, Notes, LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue, IsActive, CreatedAtUtc)
VALUES
(@name, @phone, @email, @address, @notes, @points, @discType, @discValue, @active, @createdAt);
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@phone", phone);
            cmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@address", (object?)address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@points", 0d);
            cmd.Parameters.AddWithValue("@discType", string.IsNullOrWhiteSpace(specialDiscountType) ? "None" : specialDiscountType);
            cmd.Parameters.AddWithValue("@discValue", (double)specialDiscountValue);
            cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));

            return (long)cmd.ExecuteScalar();
        }

        public static CustomerRow? GetById(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, Phone, Email, Address, Notes,
       LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue,
       IsActive, CreatedAtUtc
FROM Customers
WHERE Id=@id
LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return new CustomerRow
            {
                Id = rd.GetInt64(0),
                Name = rd.GetString(1),
                Phone = rd.GetString(2),
                Email = rd.IsDBNull(3) ? null : rd.GetString(3),
                Address = rd.IsDBNull(4) ? null : rd.GetString(4),
                Notes = rd.IsDBNull(5) ? null : rd.GetString(5),
                LoyaltyPoints = ReadDecimal(rd, 6),
                SpecialDiscountType = rd.IsDBNull(7) ? "None" : rd.GetString(7),
                SpecialDiscountValue = ReadDecimal(rd, 8),
                IsActive = rd.GetInt32(9) == 1,
                CreatedAtUtc = rd.IsDBNull(10) ? "" : rd.GetString(10),
            };
        }

        private static decimal ReadDecimal(SqliteDataReader rd, int index)
        {
            if (rd.IsDBNull(index))
                return 0m;

            var value = rd.GetValue(index);

            return value switch
            {
                decimal d => d,
                double db => Convert.ToDecimal(db, CultureInfo.InvariantCulture),
                float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
                long l => l,
                int i => i,
                string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            };
        }
    }
}