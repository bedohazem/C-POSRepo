using System;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace POS_System
{
    public static class ReceiptPrinter
    {
        public static void PrintReceipt(string title, string shopName,
            (string name, int qty, decimal price)[] items,
            decimal total, decimal paid, decimal change)
        {
            var dlg = new PrintDialog();

            // اختيار الطابعة بالاسم (بدون نافذة)
            if (!string.IsNullOrWhiteSpace(PosSettings.PrinterName))
            {
                try
                {
                    var server = new LocalPrintServer();
                    var queue = server.GetPrintQueue(PosSettings.PrinterName);
                    dlg.PrintQueue = queue;
                }
                catch
                {
                    // لو الاسم غلط، هنرجع لـ dialog (اختياري)
                    if (!PosSettings.ShowPrintDialog)
                        throw;
                }
            }

            // لو المستخدم عايز Dialog أو الطابعة ما اتحددتش
            if (PosSettings.ShowPrintDialog || dlg.PrintQueue == null)
            {
                if (dlg.ShowDialog() != true)
                    return;
            }

            var doc = BuildFlowReceipt(shopName, items, total, paid, change);
            IDocumentPaginatorSource idp = doc;

            dlg.PrintDocument(idp.DocumentPaginator, title);
        }

        private static FlowDocument BuildFlowReceipt(string shopName,
            (string name, int qty, decimal price)[] items,
            decimal total, decimal paid, decimal change)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(10),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };

            doc.Blocks.Add(new Paragraph(new Run(shopName))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            });

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));

            foreach (var it in items)
            {
                doc.Blocks.Add(new Paragraph(new Run(it.name)));
                doc.Blocks.Add(new Paragraph(new Run($"  {it.qty} x {it.price:0.00} = {(it.qty * it.price):0.00}")));
            }

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));
            doc.Blocks.Add(new Paragraph(new Run($"TOTAL : {total:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run($"PAID  : {paid:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run($"CHANGE: {change:0.00}")));

            doc.Blocks.Add(new Paragraph(new Run("Thank you!"))
            {
                Margin = new Thickness(0, 10, 0, 0),
                TextAlignment = TextAlignment.Center
            });

            return doc;
        }
    }
}
