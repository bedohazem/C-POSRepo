using System;
using System.IO;

namespace POS_System.Localization
{
    public static class AppState
    {
        private static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "POS-System");

        private static string FilePath => Path.Combine(Dir, "culture.txt");

        public static string LoadCulture(string fallback = "ar-EG")
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = File.ReadAllText(FilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { /* تجاهل */ }

            return fallback;
        }

        public static void SaveCulture(string cultureCode)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, cultureCode);
            }
            catch { /* تجاهل */ }
        }
    }
}
