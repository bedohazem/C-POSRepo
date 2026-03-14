using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace POS_System
{
    public class AuditRow
    {
        public long Id { get; set; }
        public DateTime AtUtc { get; set; }
        public string Username { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
    }

    public static class AuditRepo
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

        public static List<string> GetAuditUsers(int limit = 200)
        {
            var list = new List<string>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT DISTINCT Username
FROM AuditLogs
WHERE IFNULL(Username,'') <> ''
ORDER BY Username
LIMIT @lim;";
            cmd.Parameters.AddWithValue("@lim", Math.Max(50, Math.Min(limit, 1000)));

            using var rd = cmd.ExecuteReader();
            while (rd.Read()) list.Add(rd.GetString(0));
            return list;
        }

        public static List<AuditRow> GetAuditLogs(
            string? username = null,
            string? search = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int limit = 1000)
        {
            var list = new List<AuditRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();

            var where = "WHERE 1=1 ";

            if (!string.IsNullOrWhiteSpace(username) && username != "All users")
            {
                where += "AND Username = @user ";
                cmd.Parameters.AddWithValue("@user", username.Trim());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                where += @"AND (
                    Username LIKE @q OR
                    IFNULL(BranchName,'') LIKE @q OR
                    Action LIKE @q OR
                    IFNULL(Details,'') LIKE @q
                ) ";
                cmd.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
            }

            if (fromUtc != null)
            {
                where += "AND AtUtc >= @from ";
                cmd.Parameters.AddWithValue("@from", fromUtc.Value.ToString("O"));
            }

            if (toUtc != null)
            {
                where += "AND AtUtc <= @to ";
                cmd.Parameters.AddWithValue("@to", toUtc.Value.ToString("O"));
            }

            cmd.CommandText = $@"
SELECT Id, AtUtc, Username, IFNULL(BranchName,''), Action, IFNULL(Details,'')
FROM AuditLogs
{where}
ORDER BY Id DESC
LIMIT @lim;";

            cmd.Parameters.AddWithValue("@lim", Math.Max(50, Math.Min(limit, 5000)));

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var atStr = rd.GetString(1);
                DateTime.TryParse(atStr, null, DateTimeStyles.RoundtripKind, out var atUtc);

                list.Add(new AuditRow
                {
                    Id = rd.GetInt64(0),
                    AtUtc = atUtc,
                    Username = rd.GetString(2),
                    BranchName = rd.GetString(3),
                    Action = rd.GetString(4),
                    Details = rd.GetString(5),
                });
            }

            return list;
        }

        public static int DeleteAllAuditLogs()
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM AuditLogs;";
            return cmd.ExecuteNonQuery();
        }

        public static int DeleteAuditLogs(DateTime fromUtc, DateTime toUtc)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM AuditLogs WHERE AtUtc >= @from AND AtUtc <= @to;";
            cmd.Parameters.AddWithValue("@from", fromUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@to", toUtc.ToString("O"));
            return cmd.ExecuteNonQuery();
        }
    }
}