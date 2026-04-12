using System;
using System.Text;

namespace POS_System.printing
{
    public static class TsplPrinter
    {
        public static void PrintBarcode(string? printerName, string text, string productName, decimal price, int copies)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new Exception("No printer selected.");

            string Safe(string s) => (s ?? "").Replace("\"", "'");

            var sb = new StringBuilder();

            sb.AppendLine("SIZE 50 mm, 35 mm");
            sb.AppendLine("GAP 2 mm, 0 mm");
            sb.AppendLine("DIRECTION 1");
            sb.AppendLine("CLS");

            sb.AppendLine($"TEXT 10,10,\"0\",0,1,1,\"{Safe(productName)}\"");
            sb.AppendLine($"TEXT 300,10,\"0\",0,1,1,\"{price:0.##}\"");
            sb.AppendLine($"BARCODE 20,55,\"128\",60,1,0,2,2,\"{Safe(text)}\"");
            sb.AppendLine($"TEXT 70,125,\"0\",0,1,1,\"{Safe(text)}\"");
            sb.AppendLine($"PRINT {copies},1");

            if (!RawPrinterHelper.SendStringToPrinter(printerName, sb.ToString()))
                throw new Exception("Failed to send TSPL command to printer.");
        }
    }
}