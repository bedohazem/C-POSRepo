using Microsoft.Data.Sqlite;
using POS_System.Audit;
using System.Collections.Generic;
using System.Xml.Linq;

namespace POS_System
{
    public class BranchRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public static class BranchRepo
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

        public static List<BranchRow> GetBranches(bool includeInactive = true)
        {
            var list = new List<BranchRow>();

            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = includeInactive
                ? "SELECT Id, Name, IsActive FROM Branches ORDER BY Name;"
                : "SELECT Id, Name, IsActive FROM Branches WHERE IsActive=1 ORDER BY Name;";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new BranchRow
                {
                    Id = rd.GetInt32(0),
                    Name = rd.GetString(1),
                    IsActive = rd.GetInt32(2) == 1
                });
            }

            return list;
        }

        public static (string name, bool isActive)? GetBranch(int id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Name, IsActive FROM Branches WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return (rd.GetString(0), rd.GetInt32(1) == 1);
        }

        public static void Create(string name, bool isActive)
        {
            
            
                   using var con = Open();
                   using var cmd = con.CreateCommand();
                   cmd.CommandText = "INSERT INTO Branches(Name,IsActive) VALUES (@n,@a);";
                   cmd.Parameters.AddWithValue("@n", name.Trim());
                   cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
                   cmd.ExecuteNonQuery();

                AuditLog.Write("BRANCH_CREATE", $"name={name}, active={(isActive ? 1 : 0)}");
        }

        public static void Update(int id, string name, bool isActive)
        {
            
                    using var con = Open();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE Branches SET Name=@n, IsActive=@a WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@n", name.Trim());
                    cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
                    cmd.ExecuteNonQuery();

            AuditLog.Write("BRANCH_UPDATE", $"id={id},name={name}, active={(isActive ? 1 : 0)}");

        }
        public static void ToggleActive(int id)
        {
         
                  using var con = Open();
                  using var cmd = con.CreateCommand();
                  cmd.CommandText = "UPDATE Branches SET IsActive = CASE IsActive WHEN 1 THEN 0 ELSE 1 END WHERE Id=@id;";
                  cmd.Parameters.AddWithValue("@id", id);
                  cmd.ExecuteNonQuery();

            AuditLog.Write("BRANCH_TOGGLE_ACTIVE", $"id={id}");
        }
    }
}
