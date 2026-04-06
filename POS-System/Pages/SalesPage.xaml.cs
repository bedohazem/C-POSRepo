using POS_System.Security;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace POS_System
{
    public partial class SalesPage : Page
    {
        private enum DiscType { Percent, Amount }
        private enum PayKind { Cash, Visa, Transfer, Split }

        private bool _isRefreshingCart = false;
        private readonly DispatcherTimer _searchTimer = new();

        private bool _useLoyaltyPoints = false;
        private decimal _redeemedPoints = 0m;
        private decimal _loyaltyPointValue = 5m; // كل نقطة = 5 جنيه
        private bool _isUpdatingCustomerPicker = false;
        private List<VariantRow> _allProductPickerItems = new();
        private bool _isUpdatingProductPickerItems = false;



        // =========================
        // Inner Models
        // =========================

        private class CartItem : INotifyPropertyChanged
        {
            private int _qty = 1;
            private decimal _discountValue = 0m;
            private DiscType _discountType = DiscType.Percent;

            public long VariantId { get; set; }
            public string Name { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Size { get; set; } = "";
            public string Color { get; set; } = "";
            public decimal Price { get; set; }
            public bool IsDraft { get; set; }


            public int Qty
            {
                get => _qty;
                set
                {
                    if (_qty == value) return;
                    _qty = value;
                    NotifyCalculatedChanged();
                }
            }

            public decimal DiscountValue
            {
                get => _discountValue;
                set
                {
                    if (_discountValue == value) return;
                    _discountValue = value;
                    NotifyCalculatedChanged();
                }
            }

            public DiscType DiscountType
            {
                get => _discountType;
                set
                {
                    if (_discountType == value) return;
                    _discountType = value;
                    NotifyCalculatedChanged();
                }
            }

            public decimal LineTotal => Price * Qty;

            public decimal LineDiscountAmount
            {
                get
                {
                    if (DiscountValue <= 0) return 0m;

                    if (DiscountType == DiscType.Amount)
                        return Math.Min(DiscountValue, LineTotal);

                    var p = Math.Min(DiscountValue, 100m);
                    return LineTotal * (p / 100m);
                }
            }

            public decimal LineTotalAfterDiscount => LineTotal - LineDiscountAmount;

            public CartItem Clone()
            {
                return new CartItem
                {
                    VariantId = VariantId,
                    Name = Name,
                    Barcode = Barcode,
                    Size = Size,
                    Color = Color,
                    Price = Price,
                    Qty = Qty,
                    DiscountValue = DiscountValue,
                    DiscountType = DiscountType,
                    IsDraft = IsDraft
                };
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private void NotifyCalculatedChanged()
            {
                OnPropertyChanged(nameof(Qty));
                OnPropertyChanged(nameof(DiscountValue));
                OnPropertyChanged(nameof(DiscountType));
                OnPropertyChanged(nameof(LineTotal));
                OnPropertyChanged(nameof(LineDiscountAmount));
                OnPropertyChanged(nameof(LineTotalAfterDiscount));
            }
        }
        private class SalesInvoiceTab
        {
            public int Number { get; set; }
            public string Title => $"فاتورة {Number}";
            public DateTime Date { get; set; } = DateTime.Now;
            public ObservableCollection<CartItem> Cart { get; } = new();
            public CustomerRow? SelectedCustomer { get; set; }
            public decimal CustomerDiscountValue { get; set; }
            public DiscType CustomerDiscountType { get; set; } = DiscType.Percent;
            public decimal InvoiceDiscountValue { get; set; }
            public DiscType InvoiceDiscountType { get; set; } = DiscType.Percent;
            public bool UseLoyaltyPoints { get; set; }
            public decimal RedeemedPoints { get; set; }
            public string BarcodeDraft { get; set; } = "";
        }

        // =========================
        // Helpers
        // =========================

        private static decimal ParseDecimalSafe(string? text)
        {
            var s = (text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(s))
                return 0m;

            s = s.Replace("٫", ".").Replace(",", ".");

            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v
                : 0m;
        }

        private void FocusBarcodeBox()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {


                BarcodeBox.Focus();
                Keyboard.Focus(BarcodeBox);
                BarcodeBox.CaretIndex = BarcodeBox.Text?.Length ?? 0;
                BarcodeBox.Select(BarcodeBox.CaretIndex, 0);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }


        private void CaretIndexToEnd()
        {
            if (BarcodeBox?.Text != null)
                BarcodeBox.CaretIndex = BarcodeBox.Text.Length;
        }
        private T? FindControl<T>(string name) where T : class
            => FindName(name) as T;

        // =========================
        // State
        // =========================
        private readonly List<SalesInvoiceTab> _openInvoices = new();
        private SalesInvoiceTab? _activeInvoice;
        private int _nextInvoiceNumber = 1;
        private ObservableCollection<CartItem> CurrentCart
             => _activeInvoice?.Cart ?? _emptyCart;
        private readonly ObservableCollection<CartItem> _emptyCart = new();

        private PayKind _pendingPayKind = PayKind.Cash;

        private decimal _invoiceDiscountValue = 0m;
        private DiscType _invoiceDiscountType = DiscType.Percent;

        private CustomerRow? _selectedCustomer = null;
        private decimal _customerDiscountValue = 0m;
        private DiscType _customerDiscountType = DiscType.Percent;

        private List<CustomerRow> _customerResults = new();

        // =========================
        // Constructor
        // =========================

        public SalesPage()
        {
            InitializeComponent();

            _searchTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                LoadProductPicker(BarcodeBox.Text);
            };

            Loaded += (_, _) =>
            {
                UpdateHeader();

                if (_openInvoices.Count == 0)
                    CreateNewInvoice();

                UpdateCustomerHeader();

                LoadCustomerPicker();
                LoadProductPicker("");

                HideCustomerOverlay();

                if (AddCustomerOverlay != null)
                    AddCustomerOverlay.Visibility = Visibility.Collapsed;

                if (PaymentOverlay != null)
                    PaymentOverlay.Visibility = Visibility.Collapsed;


                FocusBarcodeBox();

            };
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // مهم جدًا: نخلي الفوكس يروح للباركود
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BarcodeBox.Focus();
                Keyboard.Focus(BarcodeBox);
            }), System.Windows.Threading.DispatcherPriority.Background);

            // بعد ما كل حاجة تخلص
            _isPageReady = true;
        }
        private void FocusBarcode()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BarcodeBox?.Focus();
                Keyboard.Focus(BarcodeBox);
                BarcodeBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // =========================
        // Invoice Tabs
        // =========================

        private void CreateNewInvoice()
        {
            var invoice = new SalesInvoiceTab
            {
                Number = _nextInvoiceNumber++,
                Date = DateTime.Now
            };

            _openInvoices.Add(invoice);
            RefreshInvoiceTabs();
            SwitchToInvoice(invoice);
        }

        private void SwitchToInvoice(SalesInvoiceTab invoice)
        {
            PersistActiveInvoiceUiState();

            _activeInvoice = invoice;
            LoadActiveInvoiceToUi();

            CartList.ItemsSource = CurrentCart;

            RefreshInvoiceTabs();
            RefreshCartAndTotals();
            FocusBarcodeBox();
        }

        private void PersistActiveInvoiceUiState()
        {
            if (_activeInvoice == null) return;

            _activeInvoice.SelectedCustomer = _selectedCustomer;
            _activeInvoice.CustomerDiscountValue = _customerDiscountValue;
            _activeInvoice.CustomerDiscountType = _customerDiscountType;

            _activeInvoice.InvoiceDiscountValue = _invoiceDiscountValue;
            _activeInvoice.InvoiceDiscountType = _invoiceDiscountType;

            _activeInvoice.UseLoyaltyPoints = _useLoyaltyPoints;
            _activeInvoice.RedeemedPoints = _redeemedPoints;

            _activeInvoice.BarcodeDraft = BarcodeBox?.Text ?? "";
        }

        private void LoadActiveInvoiceToUi()
        {
            if (_activeInvoice == null) return;

            _selectedCustomer = _activeInvoice.SelectedCustomer;
            _customerDiscountValue = _activeInvoice.CustomerDiscountValue;
            _customerDiscountType = _activeInvoice.CustomerDiscountType;

            _invoiceDiscountValue = _activeInvoice.InvoiceDiscountValue;
            _invoiceDiscountType = _activeInvoice.InvoiceDiscountType;

            _useLoyaltyPoints = _activeInvoice.UseLoyaltyPoints;
            _redeemedPoints = _activeInvoice.RedeemedPoints;

            InvoiceDiscountBox.Text = _invoiceDiscountValue > 0
                ? _invoiceDiscountValue.ToString("0.##", CultureInfo.InvariantCulture)
                : "";

            InvoiceDiscPercent.IsChecked = _invoiceDiscountType == DiscType.Percent;
            InvoiceDiscAmount.IsChecked = _invoiceDiscountType == DiscType.Amount;

            if (UsePointsCheckBox != null)
                UsePointsCheckBox.IsChecked = _useLoyaltyPoints;

            if (BarcodeBox != null)
                BarcodeBox.Text = _activeInvoice.BarcodeDraft;

            SetCustomerPickerSelection(_selectedCustomer);
            UpdateCustomerHeader();
        }

        private void RefreshInvoiceTabs()
        {
            if (InvoiceTabsPanel == null) return;

            InvoiceTabsPanel.Children.Clear();

            foreach (var invoice in _openInvoices)
            {
                var wrap = new Grid
                {
                    Margin = new Thickness(0, 0, 8, 0)
                };

                wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tabBtn = new Button
                {
                    Content = invoice.Title,
                    Tag = invoice,
                    MinWidth = 110,
                    Height = 36,
                    Padding = new Thickness(14, 0, 14, 0),
                    Style = TryFindResource(invoice == _activeInvoice
                        ? "PrimaryButtonStyle"
                        : "DarkButtonStyle") as Style
                };
                tabBtn.Click += InvoiceTab_Click;
                Grid.SetColumn(tabBtn, 0);

                var closeBtn = new Button
                {
                    Content = "×",
                    Tag = invoice,
                    Width = 32,
                    Height = 36,
                    Margin = new Thickness(6, 0, 0, 0),
                    Style = TryFindResource("DangerButtonStyle") as Style,
                    ToolTip = "Close invoice"
                };
                closeBtn.Click += CloseInvoiceTab_Click;
                Grid.SetColumn(closeBtn, 1);

                wrap.Children.Add(tabBtn);
                wrap.Children.Add(closeBtn);

                InvoiceTabsPanel.Children.Add(wrap);
            }
        }
        private void CloseInvoiceTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SalesInvoiceTab invoice)
                return;

            if (_openInvoices.Count == 1)
            {
                invoice.Cart.Clear();
                invoice.SelectedCustomer = null;
                invoice.CustomerDiscountValue = 0m;
                invoice.CustomerDiscountType = DiscType.Percent;
                invoice.InvoiceDiscountValue = 0m;
                invoice.InvoiceDiscountType = DiscType.Percent;
                invoice.UseLoyaltyPoints = false;
                invoice.RedeemedPoints = 0m;
                invoice.BarcodeDraft = "";
                invoice.Date = DateTime.Now;

                SwitchToInvoice(invoice);
                return;
            }

            var wasActive = invoice == _activeInvoice;
            var index = _openInvoices.IndexOf(invoice);

            _openInvoices.Remove(invoice);

            if (wasActive)
            {
                var nextIndex = Math.Min(index, _openInvoices.Count - 1);
                _activeInvoice = null;
                SwitchToInvoice(_openInvoices[nextIndex]);
            }
            else
            {
                RefreshInvoiceTabs();
            }
        }

        private void InvoiceTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SalesInvoiceTab invoice)
                SwitchToInvoice(invoice);
        }

        private void CloseCompletedInvoiceAndMoveNext()
        {
            if (_activeInvoice == null)
            {
                CreateNewInvoice();
                return;
            }

            var currentIndex = _openInvoices.IndexOf(_activeInvoice);
            if (currentIndex >= 0)
                _openInvoices.RemoveAt(currentIndex);

            if (_openInvoices.Count == 0)
            {
                _activeInvoice = null;
                CreateNewInvoice();
                return;
            }

            var nextIndex = Math.Min(currentIndex, _openInvoices.Count - 1);
            _activeInvoice = null;
            SwitchToInvoice(_openInvoices[nextIndex]);
        }

        public void NewInvoice_Click(object sender, RoutedEventArgs e)
        {
            CreateNewInvoice();
        }


        // =========================
        // Product / Customer Pickers
        // =========================
        private void ProductPickerCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ProductPickerCombo.IsDropDownOpen)
            {
                LoadProductPicker("");
            }
        }

        private void LoadCustomerPicker(string q = "")
        {
            var combo = FindControl<ComboBox>("CustomerPickerCombo");
            if (combo == null) return;

            combo.DisplayMemberPath = "Name";
            combo.ItemsSource = CustomerRepo.Search(q, limit: 200, activeOnly: true);
        }

        private void LoadProductPicker(string q = "")
        {
            if (_isUpdatingProductPickerItems) return;

            _isUpdatingProductPickerItems = true;

            try
            {
                List<VariantRow> items;

                if (string.IsNullOrWhiteSpace(q))
                {
                    items = ProductRepo.SearchVariantsByBarcodeOrName("", limit: 200, includeInactive: false);
                }
                else
                {
                    items = ProductRepo.SearchVariantsByBarcodeOrName(q.Trim(), limit: 200, includeInactive: false);
                }

                _allProductPickerItems = items;

                ProductPickerCombo.ItemsSource = null;
                ProductPickerCombo.ItemsSource = _allProductPickerItems;
                ProductPickerCombo.DisplayMemberPath = "ProductName";

            }
            finally
            {
                _isUpdatingProductPickerItems = false;
            }
        }

        private bool _isPageReady = false;
        private void ProductPickerCombo_DropDownOpened(object sender, EventArgs e)
        {
            // لو الصفحة لسه بتفتح → اقفلها فورًا
            if (!_isPageReady)
            {
                ProductPickerCombo.IsDropDownOpen = false;
                return;
            }

            ProductPickerCombo.ItemsSource = ProductRepo.GetAllVariants(200, false);
        }
        private void SetCustomerPickerSelection(CustomerRow? c)
        {
            var combo = FindControl<ComboBox>("CustomerPickerCombo");
            if (combo == null) return;

            _isUpdatingCustomerPicker = true;
            try
            {
                if (c == null)
                {
                    combo.ItemsSource = null;
                    combo.SelectedItem = null;
                    combo.Text = "";
                    return;
                }

                var items = CustomerRepo.Search(c.Phone ?? c.Name ?? "", limit: 50, activeOnly: true);
                combo.DisplayMemberPath = "Name";
                combo.ItemsSource = items;
                combo.SelectedItem = items.FirstOrDefault(x => x.Id == c.Id);
                combo.Text = c.Name;
            }
            finally
            {
                _isUpdatingCustomerPicker = false;
            }
        }

        private void ProductPickerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProductPickerCombo.SelectedItem is not VariantRow v)
                return;

            AddToCartVariant(v);

            ProductPickerCombo.SelectedIndex = -1;
            ProductPickerCombo.Text = "";
            ProductPickerCombo.IsDropDownOpen = false;

            BarcodeBox.Focus();
        }

        private void ProductPickerCombo_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter)
                return;

            var q = ProductPickerCombo.Text?.Trim() ?? "";

            ProductPickerCombo.ItemsSource =
                string.IsNullOrWhiteSpace(q)
                ? ProductRepo.GetAllVariants(200, false)
                : ProductRepo.SearchVariantsByBarcodeOrName(q, 200, false);

        }
        public void ProductPickerCombo_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb)
                cb.IsDropDownOpen = false;
        }
        public void CustomerPickerCombo_KeyUp(object sender, KeyEventArgs e)
        {
            if (_isUpdatingCustomerPicker) return;
            if (sender is not ComboBox cb) return;

            var q = cb.Text?.Trim() ?? "";
            cb.DisplayMemberPath = "Name";

            _isUpdatingCustomerPicker = true;
            try
            {
                cb.ItemsSource = CustomerRepo.Search(q, limit: 100, activeOnly: true);
                cb.IsDropDownOpen = !string.IsNullOrWhiteSpace(q);
            }
            finally
            {
                _isUpdatingCustomerPicker = false;
            }
        }

        private void CloseProductPickerDropdown()
        {
            if (ProductPickerCombo == null)
                return;

            ProductPickerCombo.IsDropDownOpen = false;
            ProductPickerCombo.SelectedIndex = -1;
        }

        public void CustomerPickerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingCustomerPicker) return;
            if (sender is not ComboBox cb) return;
            if (cb.SelectedItem is not CustomerRow c) return;

            SelectCustomer(c);
            BarcodeBox.Focus();
        }


        public void BarcodeSaleButton_Click(object sender, RoutedEventArgs e)
        {
            BarcodeBox.Focus();
            BarcodeBox.SelectAll();
        }

        private void ProductPickerCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
  
        }

        private void ProductPickerCombo_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {

                FocusBarcodeBox();
                e.Handled = true;
            }
        }


        // =========================
        // Search / Scan
        // =========================


        private void BarcodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_activeInvoice != null)
                _activeInvoice.BarcodeDraft = BarcodeBox.Text ?? "";

            _searchTimer.Stop();
            _searchTimer.Start();
        }
        private VariantRow? FindVariantForScan(string text)
        {
            var q = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return null;

            var exact = ProductRepo.GetVariantByBarcode(q);
            if (exact != null && exact.IsActive)
                return exact;

            return ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 1, includeInactive: false)
                              .FirstOrDefault();
        }
        private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var q = BarcodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(q)) return;

            var v = FindVariantForScan(q);
            if (v == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            if (!v.IsActive)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            AddToCartVariant(v);
            BarcodeBox.Clear();
            e.Handled = true;
        }

        private void AddToCartVariant(VariantRow v)
        {
            if (_activeInvoice == null || !v.IsActive)
                return;

            if (v.Stock <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show("Out of stock!", "Stock");
                return;
            }

            var existing = CurrentCart.FirstOrDefault(x => x.VariantId == v.Id);

            if (existing == null)
            {
                CurrentCart.Add(new CartItem
                {
                    VariantId = v.Id,
                    Name = v.ProductName,
                    Barcode = v.Barcode,
                    Size = v.Size,
                    Color = v.Color,
                    Price = v.SellPrice,
                    Qty = 1
                });
            }
            else
            {
                if (existing.Qty + 1 > v.Stock)
                {
                    System.Media.SystemSounds.Beep.Play();
                    MessageBox.Show("Not enough stock.", "Stock");
                    return;
                }

                existing.Qty++;
            }

            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        private void FocusBarcode_Click(object sender, RoutedEventArgs e)
        {
            BarcodeBox.Focus();
            BarcodeBox.SelectAll();
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F6)
            {
                FocusBarcode_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F12)
            {
                OpenPaymentOverlay(PayKind.Cash);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (AddCustomerOverlay?.Visibility == Visibility.Visible)
                {
                    AddCustomerOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    return;
                }

                if (CustomerOverlay?.Visibility == Visibility.Visible)
                {
                    HideCustomerOverlay();
                    e.Handled = true;
                    return;
                }

                if (PaymentOverlay?.Visibility == Visibility.Visible)
                {
                    PaymentOverlay.Visibility = Visibility.Collapsed;
                    BarcodeBox.Focus();
                    e.Handled = true;
                }
            }
        }

        // =========================
        // Totals / Discounts
        // =========================
        private record TotalsResult(
        decimal SubTotal,
        decimal InvoiceDiscount,
        decimal CustomerDiscount,
        decimal LoyaltyDiscount,
        decimal Total
        );

        private TotalsResult CalculateTotals()
        {
            var subTotal = CurrentCart.Sum(x => x.LineTotalAfterDiscount);

            var invoiceDisc = CalcInvoiceDiscount(subTotal);
            var afterInvoice = Math.Max(0, subTotal - invoiceDisc);

            var custDisc = CalcCustomerDiscount(afterInvoice);
            var afterCustomer = Math.Max(0, afterInvoice - custDisc);

            var loyaltyDisc = CalcLoyaltyDiscount(afterCustomer);
            var total = Math.Max(0, afterCustomer - loyaltyDisc);

            return new TotalsResult(
                SubTotal: subTotal,
                InvoiceDiscount: invoiceDisc,
                CustomerDiscount: custDisc,
                LoyaltyDiscount: loyaltyDisc,
                Total: total
            );
        }

        private void RefreshCartAndTotals()
        {
            _isRefreshingCart = true;

            if (CartList.ItemsSource != CurrentCart)
                CartList.ItemsSource = CurrentCart;

            CartList.Items.Refresh();

            _isRefreshingCart = false;

            RefreshTotalsOnly();
        }

        private void RefreshTotalsOnly()
        {
            var totals = CalculateTotals();

            SubTotalText.Text = totals.SubTotal.ToString("0.00", CultureInfo.InvariantCulture);

            var discText = totals.InvoiceDiscount.ToString("0.00", CultureInfo.InvariantCulture);

            if (_selectedCustomer != null && totals.CustomerDiscount > 0)
                discText += $" (+Cust {totals.CustomerDiscount:0.00})";

            if (_selectedCustomer != null && totals.LoyaltyDiscount > 0)
                discText += $" (+Points {totals.LoyaltyDiscount:0.00})";

            InvoiceDiscountText.Text = discText;
            TotalText.Text = totals.Total.ToString("0.00", CultureInfo.InvariantCulture);

            if (RedeemPointsText != null)
                RedeemPointsText.Text = $"Redeemed Points: {_redeemedPoints:0} | Discount: {totals.LoyaltyDiscount:0.00}";

            UpdateCustomerHeader();
            PersistActiveInvoiceUiState();
        }

        private decimal GetSubTotal() => CalculateTotals().SubTotal;

        private decimal GetTotal() => CalculateTotals().Total;

        private decimal CalcInvoiceDiscount(decimal subTotal)
        {
            if (_invoiceDiscountValue <= 0 || subTotal <= 0) return 0m;

            if (_invoiceDiscountType == DiscType.Amount)
                return Math.Min(_invoiceDiscountValue, subTotal);

            var p = Math.Min(_invoiceDiscountValue, 100m);
            return subTotal * (p / 100m);
        }

        private decimal CalcCustomerDiscount(decimal subTotalAfterLines)
        {
            if (_selectedCustomer == null) return 0m;
            if (_customerDiscountValue <= 0 || subTotalAfterLines <= 0) return 0m;

            if (_customerDiscountType == DiscType.Amount)
                return Math.Min(_customerDiscountValue, subTotalAfterLines);

            var p = Math.Min(_customerDiscountValue, 100m);
            return subTotalAfterLines * (p / 100m);
        }

        private void InvoiceDiscountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _invoiceDiscountValue = ParseDecimalSafe(InvoiceDiscountBox.Text);

            if (_invoiceDiscountValue < 0)
                _invoiceDiscountValue = 0;

            _invoiceDiscountType = (InvoiceDiscAmount.IsChecked == true)
                ? DiscType.Amount
                : DiscType.Percent;

            PersistActiveInvoiceUiState();
            RefreshTotalsOnly();
        }

        private void InvoiceDiscountOption_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            _invoiceDiscountType =
                (InvoiceDiscAmount?.IsChecked == true)
                    ? DiscType.Amount
                    : DiscType.Percent;

            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        private void LineDiscountValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isRefreshingCart) return;
            if ((sender as FrameworkElement)?.DataContext is not CartItem item) return;
            if (sender is not TextBox tb) return;

            var val = ParseDecimalSafe(tb.Text);

            if (val < 0)
                val = 0;

            item.DiscountValue = val;

            RefreshTotalsOnly();
        }

        private void LineDiscountType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingCart) return;
            if ((sender as FrameworkElement)?.DataContext is not CartItem item) return;

            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
                item.DiscountType = tag == "Amount" ? DiscType.Amount : DiscType.Percent;

            RefreshTotalsOnly();
        }

        // =========================
        // Cart ops
        // =========================

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            CurrentCart.Clear();
            _invoiceDiscountValue = 0m;
            _invoiceDiscountType = DiscType.Percent;
            InvoiceDiscountBox.Text = "";
            InvoiceDiscPercent.IsChecked = true;
            _selectedCustomer = null;
            _customerDiscountValue = 0m;
            _customerDiscountType = DiscType.Percent;
            _useLoyaltyPoints = false;
            _redeemedPoints = 0m;

            if (UsePointsCheckBox != null)
                UsePointsCheckBox.IsChecked = false;
            if (AddCustomerOverlay != null)
                AddCustomerOverlay.Visibility = Visibility.Collapsed;

            SetCustomerPickerSelection(null);
            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
            BarcodeBox.Clear();
            BarcodeBox.Focus();
        }


        private void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            var branchId = SessionManager.CurrentBranchId;
            if (branchId == null)
            {
                MessageBox.Show("Branch is missing.", "Error");
                return;
            }

            var stockNow = StockRepo.GetStock(item.VariantId, branchId.Value);

            if (item.Qty + 1 > stockNow)
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show("Not enough stock.", "Stock");
                return;
            }

            item.Qty++;
            PersistActiveInvoiceUiState();
            RefreshTotalsOnly();
        }

        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            if (item.Qty <= 1)
                return;

            item.Qty--;

            CartList.Items.Refresh();
            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            CurrentCart.Remove(item);

            CartList.Items.Refresh();
            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        // =========================
        // Payment Overlay
        // =========================

        private void PayCash_Click(object sender, RoutedEventArgs e) => OpenPaymentOverlay(PayKind.Cash);
        private void PayVisa_Click(object sender, RoutedEventArgs e) => OpenPaymentOverlay(PayKind.Visa);
        private void PayTransfer_Click(object sender, RoutedEventArgs e) => OpenPaymentOverlay(PayKind.Transfer);
        private void PaySplit_Click(object sender, RoutedEventArgs e) => OpenPaymentOverlay(PayKind.Split);

        private void OpenPaymentOverlay(PayKind kind)
        {
            var total = GetTotal();
            if (total <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            _pendingPayKind = kind;
            PayTitle.Text = $"Payment - {kind}";
            PayTotalText.Text = total.ToString("0.00", CultureInfo.InvariantCulture);
            ReceivedBox.Text = total.ToString("0.00", CultureInfo.InvariantCulture);

            ChangeText.Text = "0.00";
            PaymentOverlay.Visibility = Visibility.Visible;
            ReceivedBox.Focus();
            ReceivedBox.SelectAll();

            ReceivedBox.TextChanged -= ReceivedBox_TextChanged;
            ReceivedBox.TextChanged += ReceivedBox_TextChanged;
            ReceivedBox_TextChanged(null, null);
        }

        private void ReceivedBox_TextChanged(object? sender, TextChangedEventArgs? e)
        {
            var total = GetTotal();
            var received = ParseDecimalSafe(ReceivedBox.Text);
            var change = received - total;
            ChangeText.Text = change.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void CancelPayment_Click(object sender, RoutedEventArgs e)
        {
            PaymentOverlay.Visibility = Visibility.Collapsed;
            BarcodeBox.Focus();
        }

        private void ConfirmPayment_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentCart.Count == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var totals = CalculateTotals();
            var total = totals.Total;
            var subTotal = totals.SubTotal;

            var received = ParseDecimalSafe(ReceivedBox.Text);

            if (_pendingPayKind == PayKind.Cash && received < total)
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show("Received is less than total.", "Payment");
                return;
            }

            var paid = (_pendingPayKind == PayKind.Cash) ? received : total;
            var change = paid - total;

            var u = SessionManager.CurrentUser;
            var branchId = SessionManager.CurrentBranchId;

            if (u == null || branchId == null)
            {
                MessageBox.Show("Session missing user/branch.", "Error");
                return;
            }

            var invDiscTypeText = _invoiceDiscountType == DiscType.Amount ? "Amount" : "Percent";
            var typeText = "Sale";

            var saleLines = CurrentCart.Select(c =>
            {
                var lineDiscType = c.DiscountType == DiscType.Amount ? "Amount" : "Percent";
                return new SalesRepo.SaleLine(
                    VariantId: c.VariantId,
                    Name: string.IsNullOrWhiteSpace(c.Name) ? c.Barcode : c.Name,
                    Barcode: c.Barcode,
                    Size: c.Size,
                    Color: c.Color,
                    Qty: c.Qty,
                    UnitPrice: c.Price,
                    LineDiscountType: lineDiscType,
                    LineDiscountValue: c.DiscountValue,
                    LineTotalAfterDiscount: c.LineTotalAfterDiscount
                );
            }).ToList();

            foreach (var c in CurrentCart)
            {
                var stockNow = StockRepo.GetStock(c.VariantId, branchId);
                if (stockNow < c.Qty)
                {
                    MessageBox.Show(
                        $"Not enough stock for {c.Barcode}\nAvailable: {stockNow}\nRequested: {c.Qty}",
                        "Stock Error");
                    return;
                }
            }

            long? customerId = _selectedCustomer?.Id;

            long saleId;
            try
            {
                saleId = SalesRepo.CreateSaleV2(
                    userId: u.Id,
                    branchId: branchId.Value,
                    type: typeText,
                    customerId: customerId,
                    subTotal: subTotal,
                    invoiceDiscountType: invDiscTypeText,
                    invoiceDiscountValue: _invoiceDiscountValue,
                    grandTotal: total,
                    paid: paid,
                    change: change,
                    method: _pendingPayKind.ToString(),
                    refSaleId: null,
                    notes: null,
                    items: saleLines
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed:\n" + ex.Message, "Error");
                return;
            }

            if (_selectedCustomer != null && _redeemedPoints > 0)
            {
                LoyaltyRepo.AddTx(
                    customerId: _selectedCustomer.Id,
                    type: "REDEEM",
                    points: -_redeemedPoints,
                    refSaleId: saleId,
                    notes: $"Redeemed {_redeemedPoints:0} points as discount",
                    userId: u.Id,
                    branchId: branchId.Value
                );
            }

            if (_selectedCustomer != null)
            {
                var pointsBaseAmount = Math.Max(0, subTotal - CalcInvoiceDiscount(subTotal));
                var customerDiscountForPoints = CalcCustomerDiscount(pointsBaseAmount);
                var amountEligibleForPoints = Math.Max(0, pointsBaseAmount - customerDiscountForPoints);

                var earnedPoints = Math.Floor(amountEligibleForPoints / 100m); // كل 100 جنيه = 1 نقطة

                if (earnedPoints > 0)
                {
                    LoyaltyRepo.AddTx(
                        customerId: _selectedCustomer.Id,
                        type: "EARN",
                        points: earnedPoints,
                        refSaleId: saleId,
                        notes: "Points earned from sale",
                        userId: u.Id,
                        branchId: branchId.Value
                    );
                }

                ReloadSelectedCustomer();
            }

            if (PosSettings.PrintReceipt)
            {
                try
                {
                    PrintReceiptInternal(
                        title: "POS Receipt",
                        shopName: "POS System",
                        payKind: _pendingPayKind.ToString(),
                        paid: paid,
                        change: change,
                        total: total
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Printing failed:\n{ex.Message}");
                }
            }

            PaymentOverlay.Visibility = Visibility.Collapsed;

            MessageBox.Show(
                $"Payment: {_pendingPayKind}\nTotal: {total:0.00}\nPaid: {paid:0.00}\nChange/Remaining: {change:0.00}",
                "Sale Completed"
            );

            CloseCompletedInvoiceAndMoveNext();
            BarcodeBox.Focus();
        }

        private void LineDiscountTypeCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            if (cb.DataContext is not CartItem item) return;

            foreach (var obj in cb.Items)
            {
                if (obj is ComboBoxItem cbi && cbi.Tag is string tag)
                {
                    var wanted = item.DiscountType == DiscType.Amount ? "Amount" : "Percent";
                    if (tag == wanted)
                    {
                        cb.SelectedItem = cbi;
                        break;
                    }
                }
            }
        }

        // =========================
        // Print
        // =========================

        private void PrintReceipt_Click(object sender, RoutedEventArgs e)
        {
            var total = GetTotal();
            if (total <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            try
            {
                PrintReceiptInternal(
                    title: "POS Receipt",
                    shopName: "POS System",
                    payKind: "N/A",
                    paid: 0,
                    change: 0,
                    total: total
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Printing failed:\n{ex.Message}");
            }
        }

        private void PrintReceiptInternal(string title, string shopName, string payKind, decimal paid, decimal change, decimal total)
        {
            var dlg = new System.Windows.Controls.PrintDialog();
            if (dlg.ShowDialog() != true) return;

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
            doc.Blocks.Add(new Paragraph(new Run(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))));
            doc.Blocks.Add(new Paragraph(new Run($"Pay: {payKind}")));

            if (_selectedCustomer != null)
                doc.Blocks.Add(new Paragraph(new Run($"Customer: {_selectedCustomer.Name} ({_selectedCustomer.Phone})")));

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));

            foreach (var it in CurrentCart)
            {
                var line = $"{it.Barcode} ({it.Size}/{it.Color}) x{it.Qty}  {it.Price:0.00}  = {it.LineTotalAfterDiscount:0.00}";
                if (it.LineDiscountAmount > 0)
                    line += $"  (disc {it.LineDiscountAmount:0.00})";

                doc.Blocks.Add(new Paragraph(new Run(line)));
            }

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));

            var totals = CalculateTotals();
            var sub = totals.SubTotal;
            var invDisc = totals.InvoiceDiscount;
            var custDisc = totals.CustomerDiscount;

            doc.Blocks.Add(new Paragraph(new Run($"SubTotal: {sub:0.00}")));
            doc.Blocks.Add(new Paragraph(new Run($"InvDisc:  {invDisc:0.00}")));
            if (_selectedCustomer != null && custDisc > 0)
                doc.Blocks.Add(new Paragraph(new Run($"CustDisc: {custDisc:0.00}")));

            doc.Blocks.Add(new Paragraph(new Run($"TOTAL:    {total:0.00}")) { FontWeight = FontWeights.Bold });

            if (payKind != "N/A")
            {
                doc.Blocks.Add(new Paragraph(new Run($"Paid:     {paid:0.00}")));
                doc.Blocks.Add(new Paragraph(new Run($"Change:   {change:0.00}")));
            }

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));
            doc.Blocks.Add(new Paragraph(new Run("Thank you!")) { TextAlignment = TextAlignment.Center });

            IDocumentPaginatorSource idp = doc;
            dlg.PrintDocument(idp.DocumentPaginator, title);
        }

        // =========================
        // Header
        // =========================

        private void UpdateHeader()
        {
            var u = SessionManager.CurrentUser;
            CashierText.Text = u?.Username ?? "Unknown";

            var branchName = SessionManager.CurrentBranchName;
            BranchText.Text = string.IsNullOrWhiteSpace(branchName) ? "Branch: -" : $"Branch: {branchName}";
        }

        // =========================
        // Customers UI
        // =========================

        private void UpdateCustomerHeader()
        {
            if (SelectedCustomerSummaryText == null || CustomerDiscText == null)
                return;

            if (_selectedCustomer == null)
            {
                SelectedCustomerSummaryText.Text = "Customer: Walk-in";
                CustomerDiscText.Text = "Customer Discount: 0.00";
                return;
            }

            SelectedCustomerSummaryText.Text =
                $"Customer: {_selectedCustomer.Name} ({_selectedCustomer.Phone}) | Points: {_selectedCustomer.LoyaltyPoints:0}";

            CustomerDiscText.Text = _customerDiscountType == DiscType.Amount
                ? $"Customer Discount: {_customerDiscountValue:0.00} EGP"
                : $"Customer Discount: {_customerDiscountValue:0.##}%";
        }

        private void PickCustomer_Click(object sender, RoutedEventArgs e)
        {
            ShowCustomerOverlay();
            CustomerSearchBox.Text = "";
            RefreshCustomerResults("");
            CustomerSearchBox.Focus();
        }

        private void ClearCustomer_Click(object sender, RoutedEventArgs e)
        {
            _selectedCustomer = null;
            _customerDiscountValue = 0m;
            _customerDiscountType = DiscType.Percent;

            _useLoyaltyPoints = false;
            _redeemedPoints = 0m;

            if (UsePointsCheckBox != null)
                UsePointsCheckBox.IsChecked = false;

            SetCustomerPickerSelection(null);
            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        private void CustomerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshCustomerResults(CustomerSearchBox.Text);
        }

        private void RefreshCustomerResults(string q)
        {
            _customerResults = CustomerRepo.Search(q, limit: 200, activeOnly: true);
            CustomersList.ItemsSource = _customerResults;
        }

        private void CustomersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CustomersList.SelectedItem is CustomerRow c)
                SelectCustomer(c);
        }

        private void CustomerSelect_Click(object sender, RoutedEventArgs e)
        {
            if (CustomersList.SelectedItem is not CustomerRow c)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            SelectCustomer(c);
        }

        private void SelectCustomer(CustomerRow c)
        {
            if (!c.IsActive)
            {
                MessageBox.Show("Customer is inactive.");
                return;
            }

            _selectedCustomer = c;
            ApplySelectedCustomerDiscount();

            PersistActiveInvoiceUiState();
            HideCustomerOverlay();
            RefreshCartAndTotals();
        }

        private void ApplySelectedCustomerDiscount()
        {
            if (_selectedCustomer == null)
            {
                _customerDiscountValue = 0m;
                _customerDiscountType = DiscType.Percent;
                return;
            }

            _customerDiscountValue = _selectedCustomer.SpecialDiscountValue;
            _customerDiscountType =
                string.Equals(_selectedCustomer.SpecialDiscountType, "Amount", StringComparison.OrdinalIgnoreCase)
                    ? DiscType.Amount
                    : DiscType.Percent;
        }

        private decimal CalcLoyaltyDiscount(decimal amountAfterOtherDiscounts)
        {
            _redeemedPoints = 0m;

            if (!_useLoyaltyPoints || _selectedCustomer == null || amountAfterOtherDiscounts <= 0)
                return 0m;

            var customerPoints = Math.Max(0, _selectedCustomer.LoyaltyPoints);
            if (customerPoints <= 0 || _loyaltyPointValue <= 0)
                return 0m;

            var maxPointsUsable = Math.Floor(amountAfterOtherDiscounts / _loyaltyPointValue);
            _redeemedPoints = Math.Min(customerPoints, maxPointsUsable);

            return _redeemedPoints * _loyaltyPointValue;
        }

        private void UsePointsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _useLoyaltyPoints = UsePointsCheckBox.IsChecked == true;
            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        private void ReloadSelectedCustomer()
        {
            if (_selectedCustomer == null)
                return;

            var refreshed = CustomerRepo.GetById(_selectedCustomer.Id);
            if (refreshed == null || !refreshed.IsActive)
            {
                _selectedCustomer = null;
                _customerDiscountValue = 0m;
                _customerDiscountType = DiscType.Percent;
            }
            else
            {
                _selectedCustomer = refreshed;
                ApplySelectedCustomerDiscount();
            }

            PersistActiveInvoiceUiState();
            RefreshCartAndTotals();
        }

        private void CustomerOverlayCancel_Click(object sender, RoutedEventArgs e)
        {
            HideCustomerOverlay();
        }

        private void ShowCustomerOverlay()
        {
            if (CustomerOverlay != null)
                CustomerOverlay.Visibility = Visibility.Visible;
        }

        private void HideCustomerOverlay()
        {
            if (CustomerOverlay != null)
                CustomerOverlay.Visibility = Visibility.Collapsed;
        }

        private void OpenAddCustomerOverlay_Click(object sender, RoutedEventArgs e)
        {
            HideCustomerOverlay();

            NewCustomerNameBox.Text = "";
            NewCustomerPhoneBox.Text = "";
            AddCustomerOverlay.Visibility = Visibility.Visible;
            NewCustomerNameBox.Focus();
        }

        private void CancelQuickCustomer_Click(object sender, RoutedEventArgs e)
        {
            AddCustomerOverlay.Visibility = Visibility.Collapsed;
            ShowCustomerOverlay();
            CustomerSearchBox.Focus();
        }

        private void SaveQuickCustomer_Click(object sender, RoutedEventArgs e)
        {
            var name = (NewCustomerNameBox.Text ?? "").Trim();
            var phone = (NewCustomerPhoneBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Customer name is required.", "Validation");
                NewCustomerNameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                MessageBox.Show("Phone is required.", "Validation");
                NewCustomerPhoneBox.Focus();
                return;
            }

            try
            {
                var existing = CustomerRepo.Search(phone, limit: 20, activeOnly: false)
                    .FirstOrDefault(x => string.Equals((x.Phone ?? "").Trim(), phone, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    MessageBox.Show("This phone number is already registered.", "Duplicate");
                    return;
                }

                var newId = CustomerRepo.Add(
                    name: name,
                    phone: phone,
                    email: null,
                    address: null,
                    notes: null,
                    specialDiscountType: "None",
                    specialDiscountValue: 0m,
                    isActive: true
                );

                var created = CustomerRepo.GetById(newId);
                if (created == null)
                {
                    MessageBox.Show("Customer added, but failed to load it again.", "Warning");
                    AddCustomerOverlay.Visibility = Visibility.Collapsed;
                    RefreshCustomerResults("");
                    return;
                }

                AddCustomerOverlay.Visibility = Visibility.Collapsed;
                RefreshCustomerResults(phone);
                LoadCustomerPicker(phone);
                SelectCustomer(created);
                BarcodeBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add customer:\n" + ex.Message, "Error");
            }
        }
    }
}