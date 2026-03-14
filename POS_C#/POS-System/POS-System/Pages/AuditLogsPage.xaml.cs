using POS_System.Audit;
using System.Windows;
using System.Windows.Controls;

namespace POS_System.Pages
{
    public partial class AuditLogsPage : Page
    {
        public AuditLogsPage()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            var q = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();

            DateTime? fromUtc = null;
            DateTime? toUtc = null;

            if (FromDate.SelectedDate != null)
            {
                // بداية اليوم local ثم UTC
                var d = FromDate.SelectedDate.Value.Date;
                fromUtc = DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime();
            }

            if (ToDate.SelectedDate != null)
            {
                // نهاية اليوم local ثم UTC (inclusive)
                var d = ToDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);
                toUtc = DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime();
            }

            LogsList.ItemsSource = AuditRepo.GetAuditLogs(search: q, fromUtc: fromUtc, toUtc: toUtc, limit: 2000);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => LoadData();

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

        private void FromDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadData();

        private void ToDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadData();



        private void DeleteRange_Click(object sender, RoutedEventArgs e)
        {
            // Admin only
            var isAdmin = string.Equals(POS_System.Security.SessionManager.CurrentUser?.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase);
            if (!isAdmin) { MessageBox.Show("للمدير فقط"); return; }

            if (FromDate.SelectedDate == null || ToDate.SelectedDate == null)
            {
                MessageBox.Show("اختار From و To");
                return;
            }

            var confirm = MessageBox.Show("متأكد تمسح اللوج في المدى ده؟", "Confirm", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes) return;

            var fromUtc = DateTime.SpecifyKind(FromDate.SelectedDate.Value.Date, DateTimeKind.Local).ToUniversalTime();
            var toUtc = DateTime.SpecifyKind(ToDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime();

            var n = AuditRepo.DeleteAuditLogs(fromUtc, toUtc);
            AuditLog.Write("AUDIT_DELETE_RANGE", $"from={fromUtc:O}, to={toUtc:O}, rows={n}");

            // AuditRepo.Vacuum(); // اختياري لو مسحت كتير
            LoadData();
        }


        private void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            var isAdmin = string.Equals(POS_System.Security.SessionManager.CurrentUser?.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase);
            if (!isAdmin) { MessageBox.Show("للمدير فقط"); return; }

            var confirm = MessageBox.Show("متأكد تمسح كل اللوج؟", "Confirm", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes) return;

            var n = AuditRepo.DeleteAllAuditLogs();
            AuditLog.Write("AUDIT_DELETE_ALL", $"rows={n}");

            // AuditRepo.Vacuum(); // اختياري
            LoadData();
        }

    }

}