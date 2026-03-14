using POS_System.Security;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace POS_System
{
    public partial class SalesHistoryPage : Page
    {
        private List<SalesRepo.SaleListRow> _sales = new();
        private SalesRepo.SaleHeader? _selectedHeader;
        private List<SalesRepo.SaleItemRow> _selectedItems = new();

        public SalesHistoryPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                TypeFilterBox.SelectedIndex = 0;
                RefreshSales();
            };
        }

        private void RefreshSales()
        {
            var q = SearchBox?.Text?.Trim() ?? "";

            string? type = null;
            if (TypeFilterBox?.SelectedItem is ComboBoxItem cbi)
            {
                var tag = cbi.Tag?.ToString();
                if (!string.IsNullOrWhiteSpace(tag))
                    type = tag;
            }

            _sales = SalesRepo.GetSalesList(q, 300, type);
            SalesList.ItemsSource = null;
            SalesList.ItemsSource = _sales;

            if (_sales.Count == 0)
            {
                ClearDetails();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshSales();
        }

        private void TypeFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshSales();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshSales();
        }

        private void SalesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SalesList.SelectedItem is not SalesRepo.SaleListRow row)
            {
                ClearDetails();
                return;
            }

            _selectedHeader = SalesRepo.GetSaleHeader(row.Id);
            _selectedItems = SalesRepo.GetSaleItemsWithReturned(row.Id);

            if (_selectedHeader == null)
            {
                ClearDetails();
                return;
            }

            SaleIdText.Text = _selectedHeader.Id.ToString();
            if (DateTime.TryParse(_selectedHeader.AtUtc, out var dt))
                SaleDateText.Text = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            else
                SaleDateText.Text = _selectedHeader.AtUtc;
            MethodText.Text = _selectedHeader.PaymentMethod;
            TotalText.Text = _selectedHeader.GrandTotal.ToString("0.00", CultureInfo.InvariantCulture);
            PaidChangeText.Text = $"{_selectedHeader.Paid:0.00} / {_selectedHeader.Change:0.00}";

            CustomerText.Text = row.CustomerName;
            if (string.IsNullOrWhiteSpace(CustomerText.Text))
                CustomerText.Text = "Walk-in";

            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = _selectedItems;
        }

        private void ClearDetails()
        {
            _selectedHeader = null;
            _selectedItems = new List<SalesRepo.SaleItemRow>();

            SaleIdText.Text = "-";
            SaleDateText.Text = "-";
            MethodText.Text = "-";
            TotalText.Text = "-";
            PaidChangeText.Text = "-";
            CustomerText.Text = "-";

            ItemsList.ItemsSource = null;
        }

        private void ReprintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHeader == null || _selectedItems.Count == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            try
            {
                PrintReceiptInternal(
                    title: $"Receipt #{_selectedHeader.Id}",
                    shopName: "POS System",
                    payKind: _selectedHeader.PaymentMethod,
                    paid: _selectedHeader.Paid,
                    change: _selectedHeader.Change,
                    total: _selectedHeader.GrandTotal
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printing failed:\n" + ex.Message, "Error");
            }
        }

        private void PrintReceiptInternal(string title, string shopName, string payKind, decimal paid, decimal change, decimal total)
        {
            if (_selectedHeader == null) return;

            var dlg = new PrintDialog();
            if (PosSettings.ShowPrintDialog)
            {
                if (dlg.ShowDialog() != true) return;
            }

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(10),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12
            };

            doc.Blocks.Add(new Paragraph(new Run(shopName))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            });

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));
            doc.Blocks.Add(new Paragraph(new Run($"Invoice: #{_selectedHeader.Id}")));
            doc.Blocks.Add(new Paragraph(new Run(_selectedHeader.AtUtc)));
            doc.Blocks.Add(new Paragraph(new Run($"Type: {_selectedHeader.Type}")));
            doc.Blocks.Add(new Paragraph(new Run($"Pay: {payKind}")));
            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));

            foreach (var it in _selectedItems)
            {
                var line = $"{it.Barcode} ({it.Size}/{it.Color}) x{it.Qty}  {it.UnitPrice:0.00}  = {it.LineTotalAfterDiscount:0.00}";
                doc.Blocks.Add(new Paragraph(new Run(line)));
            }

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));
            doc.Blocks.Add(new Paragraph(new Run($"SubTotal: {_selectedHeader.SubTotal:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run($"InvDisc:  {_selectedHeader.InvoiceDiscountValue:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run($"TOTAL:    {total:0.00}")) { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(new Paragraph(new Run($"Paid:     {paid:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run($"Change:   {change:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));
            doc.Blocks.Add(new Paragraph(new Run("Thank you!")) { TextAlignment = TextAlignment.Center });

            IDocumentPaginatorSource idp = doc;
            dlg.PrintDocument(idp.DocumentPaginator, title);
        }
    }
}