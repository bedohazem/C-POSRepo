using Microsoft.Data.Sqlite;
using System;

namespace POS_System.printing
{
    public class BarcodePrintSettings
    {
        public double LabelWidthMm { get; set; } = 50;
        public double LabelHeightMm { get; set; } = 35;

        public double NameFontSize { get; set; } = 7;
        public double PriceFontSize { get; set; } = 7;
        public double BarcodeTextFontSize { get; set; } = 6.5;

        public double BarcodeWidthMm { get; set; } = 39;
        public double BarcodeHeightMm { get; set; } = 9;

        public bool ShowPrice { get; set; } = true;
        public bool ShowProductName { get; set; } = true;
        public bool ShowBarcodeText { get; set; } = true;

        public double NameLeftMm { get; set; } = 1;
        public double NameTopMm { get; set; } = 1;

        public double PriceLeftMm { get; set; } = 40;
        public double PriceTopMm { get; set; } = 1;

        public double BarcodeLeftMm { get; set; } = 5.5;
        public double BarcodeTopMm { get; set; } = 8;

        public double BarcodeTextLeftMm { get; set; } = 11;
        public double BarcodeTextTopMm { get; set; } = 18;
    }

    public static class BarcodePrintSettingsRepo
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

        public static BarcodePrintSettings Get()
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT
    LabelWidthMm,
    LabelHeightMm,
    NameFontSize,
    PriceFontSize,
    BarcodeTextFontSize,
    BarcodeWidthMm,
    BarcodeHeightMm,
    ShowPrice,
    ShowProductName,
    ShowBarcodeText,
    NameLeftMm,
    NameTopMm,
    PriceLeftMm,
    PriceTopMm,
    BarcodeLeftMm,
    BarcodeTopMm,
    BarcodeTextLeftMm,
    BarcodeTextTopMm
FROM BarcodePrintSettings
WHERE Id = 1
LIMIT 1;";

            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
                return new BarcodePrintSettings();

            return new BarcodePrintSettings
            {
                LabelWidthMm = rd.GetDouble(0),
                LabelHeightMm = rd.GetDouble(1),
                NameFontSize = rd.GetDouble(2),
                PriceFontSize = rd.GetDouble(3),
                BarcodeTextFontSize = rd.GetDouble(4),
                BarcodeWidthMm = rd.GetDouble(5),
                BarcodeHeightMm = rd.GetDouble(6),
                ShowPrice = rd.GetInt32(7) == 1,
                ShowProductName = rd.GetInt32(8) == 1,
                ShowBarcodeText = rd.GetInt32(9) == 1,
                NameLeftMm = rd.GetDouble(10),
                NameTopMm = rd.GetDouble(11),
                PriceLeftMm = rd.GetDouble(12),
                PriceTopMm = rd.GetDouble(13),
                BarcodeLeftMm = rd.GetDouble(14),
                BarcodeTopMm = rd.GetDouble(15),
                BarcodeTextLeftMm = rd.GetDouble(16),
                BarcodeTextTopMm = rd.GetDouble(17)
            };
        }

        public static void Save(BarcodePrintSettings s)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE BarcodePrintSettings
SET
    LabelWidthMm = @LabelWidthMm,
    LabelHeightMm = @LabelHeightMm,
    NameFontSize = @NameFontSize,
    PriceFontSize = @PriceFontSize,
    BarcodeTextFontSize = @BarcodeTextFontSize,
    BarcodeWidthMm = @BarcodeWidthMm,
    BarcodeHeightMm = @BarcodeHeightMm,
    ShowPrice = @ShowPrice,
    ShowProductName = @ShowProductName,
    ShowBarcodeText = @ShowBarcodeText,
    NameLeftMm = @NameLeftMm,
    NameTopMm = @NameTopMm,
    PriceLeftMm = @PriceLeftMm,
    PriceTopMm = @PriceTopMm,
    BarcodeLeftMm = @BarcodeLeftMm,
    BarcodeTopMm = @BarcodeTopMm,
    BarcodeTextLeftMm = @BarcodeTextLeftMm,
    BarcodeTextTopMm = @BarcodeTextTopMm
WHERE Id = 1;";

            cmd.Parameters.AddWithValue("@LabelWidthMm", s.LabelWidthMm);
            cmd.Parameters.AddWithValue("@LabelHeightMm", s.LabelHeightMm);
            cmd.Parameters.AddWithValue("@NameFontSize", s.NameFontSize);
            cmd.Parameters.AddWithValue("@PriceFontSize", s.PriceFontSize);
            cmd.Parameters.AddWithValue("@BarcodeTextFontSize", s.BarcodeTextFontSize);
            cmd.Parameters.AddWithValue("@BarcodeWidthMm", s.BarcodeWidthMm);
            cmd.Parameters.AddWithValue("@BarcodeHeightMm", s.BarcodeHeightMm);
            cmd.Parameters.AddWithValue("@ShowPrice", s.ShowPrice ? 1 : 0);
            cmd.Parameters.AddWithValue("@ShowProductName", s.ShowProductName ? 1 : 0);
            cmd.Parameters.AddWithValue("@ShowBarcodeText", s.ShowBarcodeText ? 1 : 0);
            cmd.Parameters.AddWithValue("@NameLeftMm", s.NameLeftMm);
            cmd.Parameters.AddWithValue("@NameTopMm", s.NameTopMm);
            cmd.Parameters.AddWithValue("@PriceLeftMm", s.PriceLeftMm);
            cmd.Parameters.AddWithValue("@PriceTopMm", s.PriceTopMm);
            cmd.Parameters.AddWithValue("@BarcodeLeftMm", s.BarcodeLeftMm);
            cmd.Parameters.AddWithValue("@BarcodeTopMm", s.BarcodeTopMm);
            cmd.Parameters.AddWithValue("@BarcodeTextLeftMm", s.BarcodeTextLeftMm);
            cmd.Parameters.AddWithValue("@BarcodeTextTopMm", s.BarcodeTextTopMm);

            cmd.ExecuteNonQuery();
        }
    }
}