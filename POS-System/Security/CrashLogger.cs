using POS_System.Security;
using System;
using System.IO;
using System.Text;

namespace POS_System
{
    public static class CrashLogger
    {
        public static string LogsFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "POS_System", "logs");

        public static void Log(Exception ex, string source = "Unhandled")
        {
            try
            {
                Directory.CreateDirectory(LogsFolder);

                var path = Path.Combine(LogsFolder, $"crash_{DateTime.Now:yyyy-MM-dd}.txt");

                var u = SessionManager.CurrentUser;
                var username = u?.Username ?? "Unknown";
                var branch = SessionManager.CurrentBranchName ?? "-";

                var sb = new StringBuilder();
                sb.AppendLine("========================================");
                sb.AppendLine($"Time     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Source   : {source}");
                sb.AppendLine($"User     : {username}");
                sb.AppendLine($"Branch   : {branch}");
                sb.AppendLine($"Message  : {ex.Message}");
                sb.AppendLine($"Type     : {ex.GetType().FullName}");
                sb.AppendLine("StackTrace:");
                sb.AppendLine(ex.ToString());
                sb.AppendLine();

                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // آخر خط دفاع، منسكتش البرنامج هنا
            }
        }
    }
}