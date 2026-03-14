using POS_System.Audit;
using POS_System.Security;
using POS_System.Windows;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;

namespace POS_System
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _idleTimer = new();
        private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(15);

        public MainWindow()
        {
            InitializeComponent();

            UpdateUserLabel();


            AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) => SessionManager.Touch()), true);
            AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler((s, e) => SessionManager.Touch()), true);
            AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) => SessionManager.Touch()), true);

            _idleTimer.Interval = TimeSpan.FromSeconds(5);
            _idleTimer.Tick += (_, _) =>
            {
                if (SessionManager.CurrentUser == null) return;
                if (SessionManager.IsLocked) return;

                var idle = DateTime.UtcNow - SessionManager.LastActivityUtc;
                if (idle >= _idleTimeout)
                {
                    SessionManager.Lock();
                    AuditLog.Write("SESSION_LOCKED", $"idle={idle.TotalSeconds:0}s");

                    // افتح LoginWindow Fullscreen
                    var ok = new LoginWindow { Owner = this }.ShowDialog();

                    if (ok == true)
                    {
                        SessionManager.Unlock();
                        SessionManager.Touch();
                        AuditLog.Write("SESSION_UNLOCKED");
                        UpdateUserLabel();
                    }
                    else
                    {
                        SessionManager.SignOut();
                        AuditLog.Write("LOGOUT_AFTER_LOCK");
                        Close();
                    }
                }
            };
            _idleTimer.Start();

            Loaded += (_, _) =>
            {
                UpdateUserLabel();
                NavList.SelectedIndex = 0;
                RootFrame.Navigate(new SalesPage());
            };
        }

        private void DoLogout()
        {
            var u = SessionManager.CurrentUser;
            AuditLog.Write("LOGOUT", $"user={u?.Username}");

            SessionManager.SignOut();
            UpdateUserLabel();

            var ok = new LoginWindow { Owner = this }.ShowDialog();

            if (ok == true)
            {
                AuditLog.Write("LOGIN_AFTER_LOGOUT", $"user={SessionManager.CurrentUser?.Username}");
                UpdateUserLabel();
                NavList.SelectedIndex = 0;
                RootFrame.Navigate(new SalesPage());
                return;
            }

            Close();
        }

        private void DoLock()
        {
            SessionManager.Lock();
            AuditLog.Write("SESSION_LOCK_MANUAL");

            var ok = new LoginWindow { Owner = this }.ShowDialog();

            if (ok == true)
            {
                SessionManager.Unlock();
                SessionManager.Touch();
                AuditLog.Write("SESSION_UNLOCKED");
                UpdateUserLabel();
                NavList.SelectedIndex = 0;
                RootFrame.Navigate(new SalesPage());
                return;
            }

            Close();
        }

        private void Lock_Click(object sender, RoutedEventArgs e) => DoLock();
        private void Logout_Click(object sender, RoutedEventArgs e) => DoLogout();

        private void UpdateUserLabel()
        {
            var u = SessionManager.CurrentUser;
            UserLabel.Text = u == null ? "" : $"User: {u.Username} ({u.Role.Name})";
            ApplyNavPermissions();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is not ListBoxItem item) return;

            switch (item.Tag?.ToString())
            {
                case "sales": RootFrame.Navigate(new SalesPage()); break;
                case "returns": RootFrame.Navigate(new ReturnsPage()); break;
                case "reports": RootFrame.Navigate(new ReportsPage()); break;
                case "settings": RootFrame.Navigate(new SettingsPage()); break;

                case "sales-history":
                    RootFrame.Navigate(new SalesHistoryPage());
                    break;

                case "users":
                    try { AuthService.Demand(Permission.Users_Manage); RootFrame.Navigate(new UsersPage()); }
                    catch (Exception ex) { MessageBox.Show(ex.Message); }
                    break;

                case "branches":
                    try { AuthService.Demand(Permission.Users_Manage); RootFrame.Navigate(new BranchesPage()); }
                    catch (Exception ex) { MessageBox.Show(ex.Message); }
                    break;

                case "products":
                    try
                    {
                        // لو عايزها للأدمن فقط:
                        AuthService.Demand(Permission.Settings_Edit);
                        RootFrame.Navigate(new ProductsPage());
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                    break;

                case "warehouse": RootFrame.Navigate(new WarehousePage()); break;

                case "customers":
                    RootFrame.Navigate(new CustomersPage());
                    break;

                case "audit":
                    RootFrame.Navigate(new POS_System.Pages.AuditLogsPage());
                    break;

            }
        }
        public void NavigateToHome()
        {
            RootFrame.Navigate(new SalesPage());
            NavList.SelectedIndex = 0;
        }

        private void ApplyNavPermissions()
        {
            // لو مش لوجين => سيب الكل ظاهر (أو أخفي كله)
            var u = SessionManager.CurrentUser;
            if (u == null) return;

            foreach (var obj in NavList.Items)
            {
                if (obj is not ListBoxItem item) continue;

                var tag = item.Tag?.ToString() ?? "";

                bool visible = tag switch
                {
                    "sales" => AuthService.HasPermission(Permission.Sale_Create),
                    "returns" => AuthService.HasPermission(Permission.Return_Create),
                    "reports" => AuthService.HasPermission(Permission.Reports_View),
                    "products" => AuthService.HasPermission(Permission.Settings_Edit),
                    "warehouse" => AuthService.HasPermission(Permission.Settings_Edit),
                    "settings" => AuthService.HasPermission(Permission.Settings_Edit),
                    "users" => AuthService.HasPermission(Permission.Users_Manage),
                    "branches" => AuthService.HasPermission(Permission.Users_Manage),
                    "audit" => string.Equals(u.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase),
                    "sales-history" => AuthService.HasPermission(Permission.Sale_Create),

                    // ✅ العملاء: خليها تظهر للكاشير والأدمن
                    "customers" => AuthService.HasPermission(Permission.Sale_Create),

                    _ => false
                };

                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }

            // لو الحالي اتخفى، روح للمبيعات
            if (NavList.SelectedItem is ListBoxItem sel && sel.Visibility != Visibility.Visible)
            {
                NavList.SelectedItem = NavList.Items
                    .OfType<ListBoxItem>()
                    .FirstOrDefault(x => (x.Tag?.ToString() == "sales") && x.Visibility == Visibility.Visible);
                RootFrame.Navigate(new SalesPage());
            }
        }

    }
}
