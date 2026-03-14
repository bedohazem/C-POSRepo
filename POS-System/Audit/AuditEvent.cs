using System;

namespace POS_System.Audit
{
    public enum AuditSeverity { Info = 1, Warning = 2, Critical = 3 }

    public class AuditEvent
    {
        public DateTime AtUtc { get; set; } = DateTime.UtcNow;

        public int? UserId { get; set; }
        public string Username { get; set; } = "UNKNOWN";

        public int? BranchId { get; set; }
        public string BranchName { get; set; } = "";

        public string Action { get; set; } = "";
        public string? EntityName { get; set; }
        public string? EntityId { get; set; }

        public AuditSeverity Severity { get; set; } = AuditSeverity.Info;

        public string Details { get; set; } = "";

        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
    }
}