namespace POS_System
{
    public static class PosSettings
    {
        public static string PrinterName { get; set; } = "Xprinter XP-350B";

        public static bool PrintReceipt { get; set; } = true;   // اختياري
        public static bool ShowPrintDialog { get; set; } = false; // لو true يطلع Dialog
    }
}
