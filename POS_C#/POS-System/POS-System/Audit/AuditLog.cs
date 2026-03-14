using Microsoft.Data.Sqlite;
using POS_System.Security;
using System;
using System.Text.Json;

namespace POS_System.Audit
{
    public static class AuditLog
    {
        public static void Write(
            string action,
            string details = "",
            string? entityName = null,
            string? entityId = null,
            object? before = null,
            object? after = null,
            AuditSeverity severity = AuditSeverity.Info)
        {
            try
            {
                var u = SessionManager.CurrentUser;

                var ev = new AuditEvent
                {
                    AtUtc = DateTime.UtcNow,
                    UserId = u?.Id,
                    Username = u?.Username ?? "UNKNOWN",
                    BranchId = SessionManager.CurrentBranchId,
                    BranchName = SessionManager.CurrentBranchName ?? "",
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    Severity = severity,
                    Details = details ?? "",
                    BeforeJson = before != null ? JsonSerializer.Serialize(before) : null,
                    AfterJson = after != null ? JsonSerializer.Serialize(after) : null
                };

                using var con = new SqliteConnection(Database.ConnStr);
                con.Open();

                using var fk = con.CreateCommand();
                fk.CommandText = "PRAGMA foreign_keys = ON;";
                fk.ExecuteNonQuery();

                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
INSERT INTO AuditLogs
(AtUtc, UserId, Username, BranchId, BranchName, Action, EntityName, EntityId, Severity, Details, BeforeJson, AfterJson)
VALUES
(@at, @uid, @un, @bid, @bname, @act, @en, @eid, @sev, @det, @before, @after);
";
                cmd.Parameters.AddWithValue("@at", ev.AtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@uid", (object?)ev.UserId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@un", ev.Username);
                cmd.Parameters.AddWithValue("@bid", (object?)ev.BranchId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bname", ev.BranchName);
                cmd.Parameters.AddWithValue("@act", ev.Action);
                cmd.Parameters.AddWithValue("@en", (object?)ev.EntityName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@eid", (object?)ev.EntityId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sev", (int)ev.Severity);
                cmd.Parameters.AddWithValue("@det", ev.Details);
                cmd.Parameters.AddWithValue("@before", (object?)ev.BeforeJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@after", (object?)ev.AfterJson ?? DBNull.Value);

                cmd.ExecuteNonQuery();
            }
            catch
            {
                // audit مايكسرش السيستم
            }
        }
    }
}