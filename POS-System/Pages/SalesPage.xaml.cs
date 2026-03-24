using POS_System.Security;
using System;
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
        private bool _isUpdatingProductPicker = false;


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

        private class InvoiceSession
        {
            public string Name { get; set; } = "";
            public List<CartItem> Cart { get; set; } = new();
        }

        private class SalesInvoiceTab
        {
            public int Number { get; set; }
            public string Title => $"فاتورة {Number}";
            public DateTime Date { get; set; } = DateTime.Now;

            public List<CartItem> Cart { get; set; } = new();

            public CustomerRow? SelectedCustomer { get; set; }
            public decimal CustomerDiscountValue { get; set; } = 0m;
            public DiscType CustomerDiscountType { get; set; } = DiscType.Percent;

            public decimal InvoiceDiscountValue { get; set; } = 0m;
            public DiscType InvoiceDiscountType { get; set; } = DiscType.Percent;

            public bool UseLoyaltyPoints { get; set; } = false;
            public decimal RedeemedPoints { get; set; } = 0m;

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


        private T? FindControl<T>(string name) where T : class
            => FindName(name) as T;

        // =========================
        // State
        // =========================

        private List<VariantRow> _searchResults = new();
        private readonly List<InvoiceSession> _invoices = new();
        private InvoiceSession? _currentInvoice = null;

        private readonly List<SalesInvoiceTab> _openInvoices = new();
        private SalesInvoiceTab? _activeInvoice;
        private int _nextInvoiceNumber = 1;

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
                RefreshSearchResults(BarcodeBox.Text);
                LoadProductPicker(BarcodeBox.Text);
            };

            Loaded += (_, _) =>
            {
                UpdateHeader();

                if (_openInvoices.Count == 0)
                    CreateNewInvoice();

                UpdateCustomerHeader();

                LoadCustomerPicker();

                var productCombo = FindControl<ComboBox>("ProductPickerCombo");
                if (productCombo != null)
                {
                    productCombo.ItemsSource = null;
                    productCombo.IsDropDownOpen = false;
                    productCombo.SelectedItem = null;
                    productCombo.Text = "";
                }


                HideCustomerOverlay();

                if (AddCustomerOverlay != null)
                    AddCustomerOverlay.Visibility = Visibility.Collapsed;

                if (PaymentOverlay != null)
                    PaymentOverlay.Visibility = Visibility.Collapsed;

                BarcodeBox.Focus();
                Keyboard.Focus(BarcodeBox);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    BarcodeBox.Focus();
                    Keyboard.Focus(BarcodeBox);
                    BarcodeBox.SelectAll();
                }), DispatcherPriority.Background);
            };
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (BarcodeBox != null)
                {
                    BarcodeBox.Focus();
                    Keyboard.Focus(BarcodeBox);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
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
            SaveUiStateToActiveInvoice();

            var tab = new SalesInvoiceTab
            {
                Number = _openInvoices.Count + 1,
                Date = DateTime.Now
            };

            _openInvoices.Add(tab);

            var invoice = new InvoiceSession
            {
                Name = tab.Title,
                Cart = new List<CartItem>()
            };

            _invoices.Add(invoice);
            _currentInvoice = invoice;
            CartList.ItemsSource = _currentInvoice.Cart;

            

            _activeInvoice = tab;

            _selectedCustomer = null;
            _customerDiscountValue = 0m;
            _customerDiscountType = DiscType.Percent;
            _invoiceDiscountValue = 0m;
            _invoiceDiscountType = DiscType.Percent;
            _useLoyaltyPoints = false;
            _redeemedPoints = 0m;

            if (InvoiceDiscountBox != null) InvoiceDiscountBox.Text = "";
            if (InvoiceDiscPercent != null) InvoiceDiscPercent.IsChecked = true;
            if (UsePointsCheckBox != null) UsePointsCheckBox.IsChecked = false;
            if (BarcodeBox != null) BarcodeBox.Text = "";

            FocusBarcode();
            SetCustomerPickerSelection(null);
            RenumberOpenInvoices();
            SyncInvoiceSessionNames();
            RefreshInvoiceTabs();
            RefreshCartAndTotals();
        }

        private void SwitchToInvoice(SalesInvoiceTab invoice)
        {
            SaveUiStateToActiveInvoice();
            FocusBarcode();
            _activeInvoice = invoice;
            LoadActiveInvoiceToUi();
            RefreshInvoiceTabs();
            RefreshCartAndTotals();

            BarcodeBox.Focus();
            Keyboard.Focus(BarcodeBox);
        }

        private void SaveUiStateToActiveInvoice()
        {
            if (_activeInvoice == null) return;

            _activeInvoice.Cart = _currentInvoice.Cart
                 .Where(x => !x.IsDraft)
                 .Select(x => x.Clone())
                 .ToList();

            _activeInvoice.SelectedCustomer = _selectedCustomer;
            _activeInvoice.CustomerDiscountValue = _customerDiscountValue;
            _activeInvoice.CustomerDiscountType = _customerDiscountType;

            _activeInvoice.InvoiceDiscountValue = _invoiceDiscountValue;
            _activeInvoice.InvoiceDiscountType = _invoiceDiscountType;

            _activeInvoice.UseLoyaltyPoints = _useLoyaltyPoints;
            _activeInvoice.RedeemedPoints = _redeemedPoints;

            _activeInvoice.BarcodeDraft = BarcodeBox?.Text ?? "";
            //_activeInvoice.Date = GetInvoiceDateFromUi();
        }

        private void LoadActiveInvoiceToUi()
        {
            if (_activeInvoice == null) return;

            _currentInvoice.Cart.Clear();
            _currentInvoice.Cart.AddRange(_activeInvoice.Cart.Select(x => x.Clone()));

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

            //SetInvoiceDateUi(_activeInvoice.Date);
            SetCustomerPickerSelection(_selectedCustomer);
           
            UpdateCustomerHeader();
        }

        private void SyncActiveInvoiceCart()
        {
            if (_activeInvoice == null) return;
            _activeInvoice.Cart = _currentInvoice.Cart.Select(x => x.Clone()).ToList();
        }

        private void RefreshInvoiceTabs()
        {
            var panel = FindControl<StackPanel>("InvoiceTabsPanel");
            if (panel == null) return;

            panel.Children.Clear();

            foreach (var invoice in _openInvoices)
            {
                var wrap = new Border
                {
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151")),
                    Background = invoice == _activeInvoice
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F46E5"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"))
                };

                var grid = new Grid
                {
                    MinWidth = 120,
                    Height = 36
                };

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tabBtn = new Button
                {
                    Content = invoice.Title,
                    Tag = invoice,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Brushes.White,
                    Padding = new Thickness(12, 0, 8, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    FocusVisualStyle = null,
                    OverridesDefaultStyle = true,
                    Template = BuildTabButtonTemplate(invoice == _activeInvoice)
                };
                tabBtn.Click += InvoiceTab_Click;
                Grid.SetColumn(tabBtn, 0);

                var closeBtn = new Button
                {
                    Content = "×",
                    Tag = invoice,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB")),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    ToolTip = "إغلاق الفاتورة",
                    FocusVisualStyle = null,
                    OverridesDefaultStyle = true,
                    Template = BuildCloseTabButtonTemplate(),
                    Visibility = _openInvoices.Count > 1 ? Visibility.Visible : Visibility.Collapsed
                };
                closeBtn.Click += CloseInvoice_Click;
                Grid.SetColumn(closeBtn, 1);

                grid.Children.Add(tabBtn);
                grid.Children.Add(closeBtn);

                wrap.Child = grid;
                panel.Children.Add(wrap);
            }
        }
        private ControlTemplate BuildTabButtonTemplate(bool isActive)
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isActive ? "#4F46E5" : "#111827")));

            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(content);
            template.VisualTree = border;

            // 👇 hover خفيف جدًا بدل الأزرق المقرف
            var hover = new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true
            };

            hover.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isActive ? "#5B52F6" : "#1F2937"))));

            template.Triggers.Add(hover);

            return template;
        }

        private ControlTemplate BuildCloseTabButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(content);
            template.VisualTree = border;

            var hoverTrigger = new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))));
            hoverTrigger.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));

            var pressedTrigger = new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true
            };
            pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"))));
            pressedTrigger.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));

            template.Triggers.Add(hoverTrigger);
            template.Triggers.Add(pressedTrigger);

            return template;
        }

        private void RenumberOpenInvoices()
        {
            for (int i = 0; i < _openInvoices.Count; i++)
            {
                _openInvoices[i].Number = i + 1;
            }

            _nextInvoiceNumber = _openInvoices.Count + 1;
        }

        private void CloseInvoice_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Button btn || btn.Tag is not SalesInvoiceTab invoice)
                return;

            if (_openInvoices.Count <= 1)
                return;

            SaveUiStateToActiveInvoice();

            var closingActive = invoice == _activeInvoice;
            var closingIndex = _openInvoices.IndexOf(invoice);

            _openInvoices.Remove(invoice);

            var linkedSession = _invoices.FirstOrDefault(x => x.Name == invoice.Title);
            if (linkedSession != null)
                _invoices.Remove(linkedSession);

            RenumberOpenInvoices();
            SyncInvoiceSessionNames();

            if (closingActive)
            {
                var nextIndex = Math.Max(0, closingIndex - 1);
                if (nextIndex >= _openInvoices.Count)
                    nextIndex = _openInvoices.Count - 1;

                _activeInvoice = _openInvoices[nextIndex];
                _currentInvoice = _invoices[nextIndex];
                LoadActiveInvoiceToUi();
            }
            else
            {
                var activeIndex = _openInvoices.IndexOf(_activeInvoice);
                if (activeIndex >= 0 && activeIndex < _invoices.Count)
                    _currentInvoice = _invoices[activeIndex];
            }

            RefreshInvoiceTabs();
            RefreshCartAndTotals();

            BarcodeBox?.Focus();
            Keyboard.Focus(BarcodeBox);
        }

        private void SyncInvoiceSessionNames()
        {
            for (int i = 0; i < _openInvoices.Count && i < _invoices.Count; i++)
            {
                _invoices[i].Name = _openInvoices[i].Title;
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

            _openInvoices.Remove(_activeInvoice);

            if (_openInvoices.Count == 0)
            {
                _activeInvoice = null;
                CreateNewInvoice();
            }
            else
            {
                _activeInvoice = null;
                SwitchToInvoice(_openInvoices[0]);
            }
        }

        public void NewInvoice_Click(object sender, RoutedEventArgs e)
        {
            CreateNewInvoice();
        }


        // =========================
        // Product / Customer Pickers
        // =========================
        public void ProductPickerCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ComboBox cb) return;

            cb.Focus();

            if (cb.ItemsSource == null || !cb.Items.Cast<object>().Any())
            {
                cb.DisplayMemberPath = "ProductName";
                cb.ItemsSource = ProductRepo.GetAllVariants(limit: 200, includeInactive: false);
            }

            cb.IsDropDownOpen = true;
            e.Handled = true;
        }
        public void ProductPickerCombo_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;

            if (cb.ItemsSource == null || !cb.Items.Cast<object>().Any())
            {
                cb.DisplayMemberPath = "ProductName";
                cb.ItemsSource = ProductRepo.GetAllVariants(limit: 200, includeInactive: false);
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
            var combo = FindControl<ComboBox>("ProductPickerCombo");
            if (combo == null) return;

            combo.DisplayMemberPath = "ProductName";

            List<VariantRow> items;

            if (string.IsNullOrWhiteSpace(q))
                items = ProductRepo.GetAllVariants(limit: 200, includeInactive: false);
            else
                items = ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 100, includeInactive: false);

            combo.ItemsSource = items;
            combo.IsDropDownOpen = items.Count > 0;
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

        public void ProductPickerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            if (cb.SelectedItem is not VariantRow v) return;

            AddToCartVariant(v);

            cb.SelectedItem = null;
            cb.Text = "";
            cb.ItemsSource = new List<VariantRow>();

            BarcodeBox.Focus();
        }

        public void ProductPickerCombo_KeyUp(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox cb) return;

            var q = cb.Text?.Trim() ?? "";
            cb.DisplayMemberPath = "ProductName";

            List<VariantRow> items;

            if (string.IsNullOrWhiteSpace(q))
                items = ProductRepo.GetAllVariants(limit: 200, includeInactive: false);
            else
                items = ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 100, includeInactive: false);

            cb.ItemsSource = items;
            cb.IsDropDownOpen = items.Count > 0;
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



        // =========================
        // Search / Scan
        // =========================

        private void RefreshSearchResults(string query)
        {
            query = (query ?? "").Trim();

            if (query.Length < 2)
            {
                _searchResults = new List<VariantRow>();
                return;
            }

            _searchResults = ProductRepo.SearchVariantsByBarcodeOrName(
                query: query,
                limit: 80,
                includeInactive: false
            );
        }

        private void BarcodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_activeInvoice != null)
                _activeInvoice.BarcodeDraft = BarcodeBox.Text ?? "";

            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var q = BarcodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(q)) return;

            var results = ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 1, includeInactive: false);
            if (results.Count == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var v = results[0];
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
            if (!v.IsActive)
                return;

            if (v.Stock <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show("Out of stock!", "Stock");
                return;
            }

            var existing = _currentInvoice.Cart.FirstOrDefault(x => x.VariantId == v.Id);

            if (existing == null)
            {
                _currentInvoice.Cart.Add(new CartItem
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

            SyncActiveInvoiceCart();
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

        private void RefreshCartAndTotals()
        {
            _isRefreshingCart = true;

            if (CartList.ItemsSource == null)
                CartList.ItemsSource = _currentInvoice.Cart;

            CartList.Items.Refresh();

            _isRefreshingCart = false;

            RefreshTotalsOnly();
        }

        private void RefreshTotalsOnly()
        {
            var subTotal = GetSubTotal();

            var invoiceDisc = CalcInvoiceDiscount(subTotal);
            var afterInvoice = Math.Max(0, subTotal - invoiceDisc);

            var custDisc = CalcCustomerDiscount(afterInvoice);
            var afterCustomer = Math.Max(0, afterInvoice - custDisc);

            var loyaltyDisc = CalcLoyaltyDiscount(afterCustomer);
            var total = Math.Max(0, afterCustomer - loyaltyDisc);

            SubTotalText.Text = subTotal.ToString("0.00", CultureInfo.InvariantCulture);

            var discText = invoiceDisc.ToString("0.00", CultureInfo.InvariantCulture);

            if (_selectedCustomer != null && custDisc > 0)
                discText += $" (+Cust {custDisc:0.00})";

            if (_selectedCustomer != null && loyaltyDisc > 0)
                discText += $" (+Points {loyaltyDisc:0.00})";

            InvoiceDiscountText.Text = discText;
            TotalText.Text = total.ToString("0.00", CultureInfo.InvariantCulture);

            if (RedeemPointsText != null)
                RedeemPointsText.Text = $"Redeemed Points: {_redeemedPoints:0} | Discount: {loyaltyDisc:0.00}";

            UpdateCustomerHeader();
            SaveUiStateToActiveInvoice();
        }

        private decimal GetSubTotal() => _currentInvoice.Cart.Sum(x => x.LineTotalAfterDiscount);

        private decimal GetTotal()
        {
            var subTotal = GetSubTotal();

            var invDisc = CalcInvoiceDiscount(subTotal);
            var afterInvoice = Math.Max(0, subTotal - invDisc);

            var custDisc = CalcCustomerDiscount(afterInvoice);
            var afterCustomer = Math.Max(0, afterInvoice - custDisc);

            var loyaltyDisc = CalcLoyaltyDiscount(afterCustomer);

            return Math.Max(0, afterCustomer - loyaltyDisc);
        }

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

            SaveUiStateToActiveInvoice();
            RefreshTotalsOnly();
        }

        private void InvoiceDiscountOption_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            _invoiceDiscountType =
                (InvoiceDiscAmount?.IsChecked == true)
                    ? DiscType.Amount
                    : DiscType.Percent;

            SaveUiStateToActiveInvoice();
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

            SyncActiveInvoiceCart();
            RefreshTotalsOnly();
        }

        private void LineDiscountType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingCart) return;
            if ((sender as FrameworkElement)?.DataContext is not CartItem item) return;

            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
                item.DiscountType = tag == "Amount" ? DiscType.Amount : DiscType.Percent;

            SyncActiveInvoiceCart();
            RefreshTotalsOnly();
        }

        // =========================
        // Cart ops
        // =========================

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _currentInvoice.Cart.Clear();

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
            //SetInvoiceDateUi(DateTime.Now);

            SyncActiveInvoiceCart();
            SaveUiStateToActiveInvoice();
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
            SyncActiveInvoiceCart();
            RefreshTotalsOnly();
        }

        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            if (item.Qty <= 1)
                return;

            item.Qty--;

            SyncActiveInvoiceCart();
            CartList.Items.Refresh();
            RefreshCartAndTotals();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            _currentInvoice.Cart.Remove(item);

            SyncActiveInvoiceCart();
            CartList.Items.Refresh();
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
            if (_currentInvoice.Cart.Count == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var total = GetTotal();
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

            var subTotal = GetSubTotal();
            var invDiscTypeText = _invoiceDiscountType == DiscType.Amount ? "Amount" : "Percent";
            var typeText = "Sale";

            var saleLines = _currentInvoice.Cart.Select(c =>
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

            foreach (var c in _currentInvoice.Cart)
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

            foreach (var it in _currentInvoice.Cart)
            {
                var line = $"{it.Barcode} ({it.Size}/{it.Color}) x{it.Qty}  {it.Price:0.00}  = {it.LineTotalAfterDiscount:0.00}";
                if (it.LineDiscountAmount > 0)
                    line += $"  (disc {it.LineDiscountAmount:0.00})";

                doc.Blocks.Add(new Paragraph(new Run(line)));
            }

            doc.Blocks.Add(new Paragraph(new Run("--------------------------------")));

            var sub = GetSubTotal();
            var invDisc = CalcInvoiceDiscount(sub);
            var afterInv = Math.Max(0, sub - invDisc);
            var custDisc = CalcCustomerDiscount(afterInv);

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
            SaveUiStateToActiveInvoice();
            RefreshCartAndTotals();
        }

        private void CustomerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshCustomerResults(CustomerSearchBox.Text);
        }

        private void RefreshCustomerResults(string q)
        {
            _customerResults = CustomerRepo.Search(q, limit: 200, activeOnly: true);
            CustomersList.ItemsSource = null;
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

            SaveUiStateToActiveInvoice();
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
            SaveUiStateToActiveInvoice();
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

            SaveUiStateToActiveInvoice();
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