
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

        // ✅ Search results
        private List<VariantRow> _searchResults = new();
        private readonly List<CartItem> _cart = new();

        private PayKind _pendingPayKind = PayKind.Cash;

        private decimal _invoiceDiscountValue = 0m;
        private DiscType _invoiceDiscountType = DiscType.Percent;

        // ===== Customers =====
        private CustomerRow? _selectedCustomer = null;
        private decimal _customerDiscountValue = 0m;
        private DiscType _customerDiscountType = DiscType.Percent;

        private List<CustomerRow> _customerResults = new();

        public SalesPage()
        {
            InitializeComponent();

            _searchTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                RefreshSearchResults(BarcodeBox.Text);
            };

            Loaded += (_, _) =>
            {
                UpdateHeader();
                RefreshSearchResults("");
                RefreshCartAndTotals();

                BarcodeBox.Focus();
                Keyboard.Focus(BarcodeBox);

                UpdateCustomerHeader();
                HideCustomerOverlay();
            };
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
                ProductsList.ItemsSource = null;
                return;
            }

            _searchResults = ProductRepo.SearchVariantsByBarcodeOrName(
                query: query,
                limit: 80,
                includeInactive: false
            );

            ProductsList.ItemsSource = null;
            ProductsList.ItemsSource = _searchResults;
        }

        private void BarcodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        // ✅ Enter: add first result (by barcode OR name)
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

        private void ProductsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProductsList.SelectedItem is not VariantRow v) return;
            if (!v.IsActive) return;
            AddToCartVariant(v);
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

            var existing = _cart.FirstOrDefault(x => x.VariantId == v.Id);

            if (existing == null)
            {
                _cart.Add(new CartItem
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

            RefreshCartAndTotals();
        }

        // =========================
        // Totals / Discounts
        // =========================

        private void RefreshCartAndTotals()
        {
            _isRefreshingCart = true;

            if (CartList.ItemsSource == null)
                CartList.ItemsSource = _cart;

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
        }

        private decimal GetSubTotal() => _cart.Sum(x => x.LineTotalAfterDiscount);

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

            RefreshTotalsOnly();
        }

        private void InvoiceDiscountOption_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            _invoiceDiscountType =
                (InvoiceDiscAmount?.IsChecked == true)
                    ? DiscType.Amount
                    : DiscType.Percent;

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
            _cart.Clear();

            _invoiceDiscountValue = 0m;
            _invoiceDiscountType = DiscType.Percent;
            InvoiceDiscountBox.Text = "";
            InvoiceDiscPercent.IsChecked = true;

            _selectedCustomer = null;
            _customerDiscountValue = 0m;
            _customerDiscountType = DiscType.Percent;

            RefreshCartAndTotals();
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
            RefreshTotalsOnly();
        }

        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            item.Qty--;

            if (item.Qty <= 0)
                _cart.Remove(item);

            RefreshCartAndTotals();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CartItem item)
                return;

            _cart.Remove(item);
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
            if (_cart.Count == 0)
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

            var saleLines = _cart.Select(c =>
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

            foreach (var c in _cart)
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

            _cart.Clear();
            RefreshCartAndTotals();
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

            foreach (var it in _cart)
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
            if (SelectedCustomerText == null || CustomerDiscText == null)
                return;

            if (_selectedCustomer == null)
            {
                SelectedCustomerText.Text = "Customer: Walk-in";
                CustomerDiscText.Text = "Customer Discount: 0.00";
                return;
            }

            SelectedCustomerText.Text =
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
            NewCustomerNameBox.Text = "";
            NewCustomerPhoneBox.Text = "";
            AddCustomerOverlay.Visibility = Visibility.Visible;
            NewCustomerNameBox.Focus();
        }

        private void CancelQuickCustomer_Click(object sender, RoutedEventArgs e)
        {
            AddCustomerOverlay.Visibility = Visibility.Collapsed;
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
                SelectCustomer(created);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add customer:\n" + ex.Message, "Error");
            }
        }
    }
}