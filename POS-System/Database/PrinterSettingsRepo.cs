using Microsoft.Data.Sqlite;

namespace POS_System.printing
{
    public class PrinterSettings
    {
        public string? PrinterName { get; set; }
        public string PrintMode { get; set; } = "Windows";
    }

    public static class PrinterSettingsRepo
    {
        private static SqliteConnection Open()
        {
            var con = new SqliteConnection(Database.ConnStr);
            con.Open();
            return con;
        }

        public static PrinterSettings Get()
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT PrinterName, PrintMode FROM PrinterSettings WHERE Id=1";

            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
                return new PrinterSettings();

            return new PrinterSettings
            {
                PrinterName = rd.IsDBNull(0) ? "" : rd.GetString(0),
                PrintMode = rd.IsDBNull(1) ? "Windows" : rd.GetString(1)
            };
        }

        public static void Save(PrinterSettings s)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE PrinterSettings
SET PrinterName=@p, PrintMode=@m
WHERE Id=1";

            cmd.Parameters.AddWithValue("@p", s.PrinterName ?? "");
            cmd.Parameters.AddWithValue("@m", s.PrintMode);

            cmd.ExecuteNonQuery();
        }
    }
}