using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using POS_System.Audit;

namespace POS_System
{
    public class UserRow
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string RoleName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class LookupItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    public static class UserRepo
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

        public static List<UserRow> GetUsers()
        {
            var list = new List<UserRow>();

            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT u.Id, u.Username, u.IsActive,
       r.Name AS RoleName,
       b.Name AS BranchName
FROM Users u
JOIN Roles r ON r.Id = u.RoleId
JOIN Branches b ON b.Id = u.BranchId
ORDER BY u.Id DESC;";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new UserRow
                {
                    Id = rd.GetInt32(0),
                    Username = rd.GetString(1),
                    IsActive = rd.GetInt32(2) == 1,
                    RoleName = rd.GetString(3),
                    BranchName = rd.GetString(4)
                });
            }
            return list;
        }

        public static List<LookupItem> GetBranches()
        {
            // Active only (منع اختيار فرع مقفول)
            return BranchRepo.GetBranches(includeInactive: false)
                .ConvertAll(b => new LookupItem { Id = b.Id, Name = b.Name });
        }

        public static List<LookupItem> GetRoles()
        {
            var list = new List<LookupItem>();

            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Roles ORDER BY Name;";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                list.Add(new LookupItem { Id = rd.GetInt32(0), Name = rd.GetString(1) });

            return list;
        }

        public static (string username, int roleId, int branchId, bool isActive)? GetUser(int id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Username, RoleId, BranchId, IsActive FROM Users WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return (rd.GetString(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3) == 1);
        }

        // ===== Multi-branches =====

        public static HashSet<int> GetUserBranchIds(int userId)
        {
            var set = new HashSet<int>();

            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT BranchId FROM UserBranches WHERE UserId=@u;";
            cmd.Parameters.AddWithValue("@u", userId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                set.Add(rd.GetInt32(0));

            return set;
        }

        public static void SetUserBranches(int userId, List<int> branchIds)
        {
            if (branchIds == null || branchIds.Count == 0)
                throw new Exception("User must have at least one branch.");

            using var con = Open();

            // تأكد إن كل الفروع Active
            foreach (var bid in branchIds.Distinct())
            {
                if (!IsBranchActive(con, bid))
                    throw new Exception("One of selected branches is inactive.");
            }

            using var tx = con.BeginTransaction();

            using (var del = con.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM UserBranches WHERE UserId=@u;";
                del.Parameters.AddWithValue("@u", userId);
                del.ExecuteNonQuery();
            }

            foreach (var bid in branchIds.Distinct())
            {
                using var ins = con.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO UserBranches(UserId, BranchId) VALUES (@u,@b);";
                ins.Parameters.AddWithValue("@u", userId);
                ins.Parameters.AddWithValue("@b", bid);
                ins.ExecuteNonQuery();
            }

            tx.Commit();

            AuditLog.Write(
                "USER_SET_BRANCHES",
                $"userId={userId}, branches=[{string.Join(",", branchIds.Distinct())}]"
            );
        }

        // ===== Create / Update =====

        public static int CreateUserReturnId(string username, string password, int roleId, int branchId, bool isActive)
        {
            using var con = Open();

            if (!IsBranchActive(con, branchId))
                throw new Exception("Selected branch is inactive.");

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Users(Username, PasswordHash, RoleId, BranchId, IsActive)
VALUES (@u, @h, @r, @b, @a);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.Parameters.AddWithValue("@h", POS_System.Security.AuthService.Hash(password));
            cmd.Parameters.AddWithValue("@r", roleId);
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);

            var newIdObj = cmd.ExecuteScalar();
            var newId = (newIdObj == null || newIdObj == DBNull.Value) ? 0L : (long)newIdObj;

            AuditLog.Write(
                "USER_CREATE",
                $"id={newId}, username={username}, roleId={roleId}, branchId={branchId}, active={(isActive ? 1 : 0)}"
            );
            return (int)newId;
        }

        public static void CreateUser(string username, string password, int roleId, int branchId, bool isActive)
        {
            _ = CreateUserReturnId(username, password, roleId, branchId, isActive);
        }

        public static void UpdateUser(int id, string username, int roleId, int branchId, bool isActive)
        {
            using var con = Open();

            if (!IsBranchActive(con, branchId))
                throw new Exception("Selected branch is inactive.");

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE Users
SET Username=@u, RoleId=@r, BranchId=@b, IsActive=@a
WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.Parameters.AddWithValue("@r", roleId);
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);

            cmd.ExecuteNonQuery();

            AuditLog.Write(
                "USER_UPDATE",
                $"id={id}, username={username}, roleId={roleId}, branchId={branchId}, active={(isActive ? 1 : 0)}"
            );
        }

        public static void ToggleActive(int id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Users SET IsActive = CASE IsActive WHEN 1 THEN 0 ELSE 1 END WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            AuditLog.Write("USER_TOGGLE_ACTIVE", $"id={id}");
        }

        public static void ResetPassword(int id, string newPassword)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Users SET PasswordHash=@h WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@h", POS_System.Security.AuthService.Hash(newPassword));
            cmd.ExecuteNonQuery();

            AuditLog.Write("USER_RESET_PW", $"id={id}");
        }

        // ===== Helpers =====

        private static bool IsBranchActive(SqliteConnection con, int branchId)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT IsActive FROM Branches WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", branchId);

            var val = cmd.ExecuteScalar();
            if (val == null || val == DBNull.Value) return false;

            return Convert.ToInt32(val) == 1;
        }
    }
}
