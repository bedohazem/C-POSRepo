using System;

namespace POS_System.Security
{
    public static class SessionManager
    {
        public static User? CurrentUser { get; private set; }

        public static int? CurrentBranchId { get; private set; }
        public static string CurrentBranchName { get; private set; } = "";

        public static bool IsAuthenticated => CurrentUser != null;

        public static bool IsLocked { get; private set; }
        public static DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

        public static void SignIn(User user, int branchId, string branchName)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            CurrentUser = user;
            CurrentBranchId = branchId;
            CurrentBranchName = branchName ?? "";
            IsLocked = false;
            Touch();
            Audit.AuditLog.Write("LOGIN_SUCCESS", $"Branch={branchName}");
        }

        public static void SignOut()
        {
            Audit.AuditLog.Write("LOGOUT", "");
            CurrentUser = null;
            CurrentBranchId = null;
            CurrentBranchName = "";
            IsLocked = false;

        }

        public static void Lock()
        {
            Audit.AuditLog.Write("SESSION_LOCK", "");
            if (!IsAuthenticated) return;
            IsLocked = true;
        }

        public static void Unlock()
        {
            if (!IsAuthenticated) return;
            IsLocked = false;
            Touch();
            Audit.AuditLog.Write("SESSION_UNLOCK", "");
        }

        public static void Touch() => LastActivityUtc = DateTime.UtcNow;

        public static bool IsSessionExpired(int timeoutMinutes)
        {
            if (!IsAuthenticated) return true;
            return (DateTime.UtcNow - LastActivityUtc).TotalMinutes >= timeoutMinutes;
        }
    }
}
