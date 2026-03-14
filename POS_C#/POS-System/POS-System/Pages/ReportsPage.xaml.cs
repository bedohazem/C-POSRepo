using POS_System.Security;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace POS_System
{
    public partial class ReportsPage : Page
    {
        public ReportsPage()
        {
            InitializeComponent();

            BranchLabel.Text = SessionManager.CurrentBranchId == null
                ? "Branch: (All)"
                : $"Branch: {SessionManager.CurrentBranchName}";

            // default: last 7 days
            ToDate.SelectedDate = DateTime.Today;
            FromDate.SelectedDate = DateTime.Today.AddDays(-6);

            RefreshAll();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

        private void RefreshAll()
        {
            var from = FromDate.SelectedDate ?? DateTime.Today;
            var to = ToDate.SelectedDate ?? DateTime.Today;

            if (to < from)
            {
                MessageBox.Show("التاريخ (إلى) لازم يكون بعد (من).");
                return;
            }

            var bid = SessionManager.CurrentBranchId;

            // ===== Summary =====
            var sum = ReportRepo.GetSalesSummary(from, to, bid);
            SalesInvoicesText.Text = sum.Invoices.ToString(CultureInfo.InvariantCulture);
            NetSalesText.Text = sum.NetSales.ToString("0.00", CultureInfo.InvariantCulture);
            SubTotalText.Text = sum.SubTotal.ToString("0.00", CultureInfo.InvariantCulture);
            DiscountsText.Text = sum.Discounts.ToString("0.00", CultureInfo.InvariantCulture);
            ReturnsText.Text = sum.Returns.ToString("0.00", CultureInfo.InvariantCulture);

            // ===== Sales by period =====
            var group = (GroupByBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "يومي";
            SalesByPeriodList.ItemsSource = null;
            SalesByPeriodList.ItemsSource = group == "شهري"
                ? ReportRepo.GetSalesByMonth(from, to, bid)
                : ReportRepo.GetSalesByDay(from, to, bid);

            // ===== Profit =====
            ProfitList.ItemsSource = null;
            ProfitList.ItemsSource = ReportRepo.GetProfitByDay(from, to, bid);

            // ===== Products =====
            TopProductsList.ItemsSource = null;
            TopProductsList.ItemsSource = ReportRepo.GetTopProducts(from, to, topN: 20, branchId: bid, ascending: false);

            LowProductsList.ItemsSource = null;
            LowProductsList.ItemsSource = ReportRepo.GetTopProducts(from, to, topN: 20, branchId: bid, ascending: true);

            // ===== Payment methods =====
            PaymentsList.ItemsSource = null;
            PaymentsList.ItemsSource = ReportRepo.GetPaymentMethods(from, to, bid);

            // ===== Cashiers =====
            CashiersList.ItemsSource = null;
            CashiersList.ItemsSource = ReportRepo.GetCashierPerformance(from, to, bid);
        }
    }
}