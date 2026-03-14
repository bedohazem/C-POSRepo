using POS_System.Security;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace POS_System
{
    public partial class WarehousePage : Page
    {
        private enum Mode { None, StockIn, Adjust }
        private Mode _mode = Mode.None;

        private ProductRepo.InventoryRow? _selected;
       

        // Purchases cart row
        private class PurchaseCartRow
        {
            public long VariantId { get; set; }
            public string ProductName { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string Size { get; set; } = "";
            public string Color { get; set; } = "";
            public decimal Qty { get; set; }
            public decimal UnitCost { get; set; }
            public decimal LineTotal => Qty * UnitCost;
        }

        private readonly List<PurchaseCartRow> _cart = new();

        // For picker overlay
        private List<VariantRow> _pickerCandidates = new();
        private enum PickerUse { None, PurchaseAdd, PurchaseSearchOnly, StocktakePick }
        private PickerUse _pickerUse = PickerUse.None;

        private bool _uiReady = false;
        public WarehousePage()
        {
            InitializeComponent();

            // ✅ خليه false لحد ما نخلص Setup
            _uiReady = false;

            BranchText.Text = SessionManager.CurrentBranchId == null
                ? "Branch: (All)"
                : $"Branch: {SessionManager.CurrentBranchName}";

            LoadSuppliers();
            RefreshInventory();
            RefreshReport();

            // ✅ دلوقتي UI جاهز
            _uiReady = true;

            RecalcTotals();

        }

        // =========================
        // Inventory
        // =========================

        private List<ProductRepo.InventoryRow> _lastInventory = new();

        private void RefreshInventory()
        {
            var list = ProductRepo.GetInventory(GlobalSearchBox.Text, includeInactive: true);

            // low stock count before filter
            var lowCount = list.Count(x => x.IsLowStock);

            if (LowStockOnlyBox.IsChecked == true)
                list = list.Where(x => x.IsLowStock).ToList();

            _lastInventory = list;
            InventoryList.ItemsSource = null;
            InventoryList.ItemsSource = list;

            LowStockCountText.Text = lowCount.ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshReport()
        {
            // reuse inventory but compute value at cost
            var list = ProductRepo.GetInventory(GlobalSearchBox.Text, includeInactive: true)
                .Select(x => new
                {
                    x.ProductName,
                    x.Barcode,
                    x.Size,
                    x.Color,
                    x.CostPrice,
                    x.Stock,
                    ValueAtCost = Math.Round(x.CostPrice * x.Stock, 2)
                })
                .ToList();

            ReportList.ItemsSource = null;
            ReportList.ItemsSource = list;

            var totalValue = list.Sum(x => x.ValueAtCost);
            InventoryValueText.Text = totalValue.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshInventory();
            RefreshReport();
        }

        private void LowStockOnlyBox_Changed(object sender, RoutedEventArgs e) => RefreshInventory();

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshInventory();
            RefreshReport();
            LoadSuppliers();
        }

        private void StockIn_Click(object sender, RoutedEventArgs e)
        {
            if (InventoryList.SelectedItem is not ProductRepo.InventoryRow row)
            {
                MessageBox.Show("اختار Variant الأول.");
                return;
            }

            _selected = row;
            _mode = Mode.StockIn;

            // reuse old overlay? (you removed it in new UI, so keep the old quick actions by using Adjust flow via dialog)
            var qty = PromptQty("Stock In (+)", $"{row.ProductName} | {row.Barcode} | {row.Size}/{row.Color} | Stock: {row.Stock}", mustBePositive: true);
            if (qty == null) return;

            SaveMovement(row.VariantId, qty.Value, "PURCHASE", null);
        }

        private void Adjust_Click(object sender, RoutedEventArgs e)
        {
            if (InventoryList.SelectedItem is not ProductRepo.InventoryRow row)
            {
                MessageBox.Show("اختار Variant الأول.");
                return;
            }

            _selected = row;
            _mode = Mode.Adjust;

            var qty = PromptQty("Adjust (+/-)", $"{row.ProductName} | {row.Barcode} | {row.Size}/{row.Color} | Stock: {row.Stock}", mustBePositive: false);
            if (qty == null) return;
            if (qty.Value == 0)
            {
                MessageBox.Show("Adjust لازم يكون + أو - (مش صفر).");
                return;
            }

            SaveMovement(row.VariantId, qty.Value, "ADJUST", null);
        }

        private decimal? PromptQty(string title, string info, bool mustBePositive)
        {
            // simple input dialog
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                $"{info}\n\nاكتب الكمية:",
                title,
                "1");

            if (string.IsNullOrWhiteSpace(input)) return null;

            if (!decimal.TryParse(input.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
            {
                MessageBox.Show("الكمية غير صحيحة.");
                return null;
            }

            if (mustBePositive && qty <= 0)
            {
                MessageBox.Show("لازم رقم موجب.");
                return null;
            }

            return qty;
        }

        private void SaveMovement(long variantId, decimal qty, string type, long? refId, string? notes = null)
        {
            try
            {
                var u = SessionManager.CurrentUser;
                var b = SessionManager.CurrentBranchId;

                StockRepo.AddMovement(
                    variantId: variantId,
                    qty: qty,
                    type: type,
                    refId: refId,
                    notes: notes,
                    userId: u?.Id,
                    branchId: b
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل حفظ المخزون:\n" + ex.Message);
                return;
            }

            RefreshInventory();
            RefreshReport();
        }

        // =========================
        // Suppliers
        // =========================

        private void LoadSuppliers()
        {
            try
            {
                var suppliers = PurchaseRepo.GetSuppliers(null, includeInactive: false);
                SupplierBox.ItemsSource = suppliers;

                if (SupplierBox.SelectedItem == null && suppliers.Count > 0)
                    SupplierBox.SelectedIndex = 0;
            }
            catch
            {
                // ignore if tables not created yet
            }
        }

        private void AddSupplier_Click(object sender, RoutedEventArgs e)
        {
            SupNameBox.Text = "";
            SupPhoneBox.Text = "";
            SupAddressBox.Text = "";
            SupplierOverlay.Visibility = Visibility.Visible;
        }

        private void CancelSupplier_Click(object sender, RoutedEventArgs e)
        {
            SupplierOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveSupplier_Click(object sender, RoutedEventArgs e)
        {
            var name = (SupNameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("اسم المورد مطلوب.");
                return;
            }

            try
            {
                PurchaseRepo.CreateSupplier(name, SupPhoneBox.Text?.Trim(), SupAddressBox.Text?.Trim(), true);
                SupplierOverlay.Visibility = Visibility.Collapsed;
                LoadSuppliers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل إضافة المورد:\n" + ex.Message);
            }
        }

        // =========================
        // Purchases
        // =========================

        private void PurchaseItemQueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SearchVariant_Click(sender, e);
                e.Handled = true;
            }
        }

        private void SearchVariant_Click(object sender, RoutedEventArgs e)
        {
            var q = (PurchaseItemQueryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q)) return;

            var results = ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 50, includeInactive: true);

            if (results.Count == 0)
            {
                MessageBox.Show("مفيش نتائج.");
                return;
            }

            if (results.Count == 1)
            {
                // fill cost automatically
                PurchaseUnitCostBox.Text = results[0].CostPrice.ToString("0.00", CultureInfo.InvariantCulture);
                MessageBox.Show($"تم اختيار: {results[0].ProductName} | {results[0].Barcode} | {results[0].Size}/{results[0].Color}");
                return;
            }

            // multiple -> picker
            _pickerCandidates = results;
            _pickerUse = PickerUse.PurchaseSearchOnly;
            PickerHintText.Text = $"نتائج البحث: {q}";
            PickerList.ItemsSource = results;
            PickerOverlay.Visibility = Visibility.Visible;
        }

        private VariantRow? ResolveVariantForPurchase()
        {
            var q = (PurchaseItemQueryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q)) return null;

            var results = ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 50, includeInactive: true);
            if (results.Count == 0) return null;
            if (results.Count == 1) return results[0];

            // multiple -> picker
            _pickerCandidates = results;
            _pickerUse = PickerUse.PurchaseAdd;
            PickerHintText.Text = $"اختر الفاريانت لإضافته للفاتورة: {q}";
            PickerList.ItemsSource = results;
            PickerOverlay.Visibility = Visibility.Visible;
            return null;
        }

        private void AddPurchaseItem_Click(object sender, RoutedEventArgs e)
        {
            // qty
            if (!decimal.TryParse((PurchaseQtyBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                MessageBox.Show("Qty غير صحيح.");
                return;
            }

            // unit cost
            if (!decimal.TryParse((PurchaseUnitCostBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var unitCost) || unitCost < 0)
            {
                MessageBox.Show("Unit Cost غير صحيح.");
                return;
            }

            var variant = ResolveVariantForPurchase();
            if (variant == null)
            {
                // either no results, or picker is shown
                if (PickerOverlay.Visibility != Visibility.Visible)
                    MessageBox.Show("اكتب باركود أو اسم صحيح.");
                return;
            }

            AddVariantToCart(variant, qty, unitCost);
        }

        private void AddVariantToCart(VariantRow variant, decimal qty, decimal unitCost)
        {
            // merge if same variant already exists
            var existing = _cart.FirstOrDefault(x => x.VariantId == variant.Id && x.UnitCost == unitCost);
            if (existing != null)
            {
                existing.Qty += qty;
            }
            else
            {
                _cart.Add(new PurchaseCartRow
                {
                    VariantId = variant.Id,
                    ProductName = variant.ProductName,
                    Barcode = variant.Barcode,
                    Size = variant.Size,
                    Color = variant.Color,
                    Qty = qty,
                    UnitCost = unitCost
                });
            }

            PurchaseItemsList.ItemsSource = null;
            PurchaseItemsList.ItemsSource = _cart.ToList();

            // auto clear query to speed next scan
            PurchaseItemQueryBox.Text = "";
            PurchaseQtyBox.Text = "1";
            PurchaseUnitCostBox.Text = "0";
            RecalcTotals();
        }

        private void RemovePurchaseItem_Click(object sender, RoutedEventArgs e)
        {
            if (PurchaseItemsList.SelectedItem is not PurchaseCartRow row) return;
            _cart.Remove(row);

            PurchaseItemsList.ItemsSource = null;
            PurchaseItemsList.ItemsSource = _cart.ToList();

            RecalcTotals();
        }

        private void Totals_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_uiReady) return;
            RecalcTotals();
        }

        private void RecalcTotals()
        {
            if (!_uiReady) return;
            if (SubTotalText == null || DiscountBox == null || PaidBox == null || TotalText == null || DueText == null)
                return;

            var sub = _cart.Sum(x => x.LineTotal);
            SubTotalText.Text = sub.ToString("0.00", CultureInfo.InvariantCulture);

            decimal discount = 0;
            decimal paid = 0;

            decimal.TryParse((DiscountBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out discount);
            decimal.TryParse((PaidBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out paid);

            var total = sub - discount;
            if (total < 0) total = 0;

            var due = total - paid;
            if (due < 0) due = 0;

            TotalText.Text = total.ToString("0.00", CultureInfo.InvariantCulture);
            DueText.Text = due.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void SavePurchase_Click(object sender, RoutedEventArgs e)
        {
            if (SupplierBox.SelectedItem is not SupplierRow sup)
            {
                MessageBox.Show("اختار مورد.");
                return;
            }

            if (_cart.Count == 0)
            {
                MessageBox.Show("مفيش أصناف في الفاتورة.");
                return;
            }

            if (SessionManager.CurrentBranchId == null)
            {
                MessageBox.Show("BranchId غير محدد.");
                return;
            }

            decimal discount = 0;
            decimal paid = 0;

            if (!decimal.TryParse((DiscountBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out discount))
            {
                MessageBox.Show("Discount غير صحيح.");
                return;
            }
            if (!decimal.TryParse((PaidBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out paid))
            {
                MessageBox.Show("Paid غير صحيح.");
                return;
            }

            var items = _cart.Select(x => new PurchaseItemInput
            {
                VariantId = x.VariantId,
                Qty = x.Qty,
                UnitCost = x.UnitCost
            }).ToList();

            var notes = string.IsNullOrWhiteSpace(PurchaseNotesBox.Text) ? null : PurchaseNotesBox.Text.Trim();

            try
            {
                var purchaseId = PurchaseRepo.CreatePurchase(
                    supplierId: sup.Id,
                    branchId: SessionManager.CurrentBranchId.Value,
                    userId: SessionManager.CurrentUser?.Id,
                    discount: discount,
                    paid: paid,
                    notes: notes,
                    items: items);

                MessageBox.Show($"تم حفظ فاتورة المشتريات رقم: {purchaseId}");

                // reset cart
                _cart.Clear();
                PurchaseItemsList.ItemsSource = null;
                DiscountBox.Text = "0";
                PaidBox.Text = "0";
                PurchaseNotesBox.Text = "";
                RecalcTotals();

                RefreshInventory();
                RefreshReport();
            }
            catch (Exception ex)
            {
                MessageBox.Show("فشل حفظ الفاتورة:\n" + ex.Message);
            }
        }

        // Picker overlay
        private void PickerCancel_Click(object sender, RoutedEventArgs e)
        {
            PickerOverlay.Visibility = Visibility.Collapsed;
            _pickerCandidates = new();
            _pickerUse = PickerUse.None;
        }

        private void PickerSelect_Click(object sender, RoutedEventArgs e)
        {
            if (PickerList.SelectedItem is not VariantRow v)
            {
                MessageBox.Show("اختار عنصر.");
                return;
            }

            PickerOverlay.Visibility = Visibility.Collapsed;

            if (_pickerUse == PickerUse.PurchaseSearchOnly)
            {
                // just set cost
                PurchaseItemQueryBox.Text = v.Barcode;
                PurchaseUnitCostBox.Text = v.CostPrice.ToString("0.00", CultureInfo.InvariantCulture);
                _pickerUse = PickerUse.None;
                return;
            }

            if (_pickerUse == PickerUse.PurchaseAdd)
            {
                // now add using current qty/cost boxes
                if (!decimal.TryParse((PurchaseQtyBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
                {
                    MessageBox.Show("Qty غير صحيح.");
                    return;
                }
                if (!decimal.TryParse((PurchaseUnitCostBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var unitCost) || unitCost < 0)
                {
                    MessageBox.Show("Unit Cost غير صحيح.");
                    return;
                }

                AddVariantToCart(v, qty, unitCost);
                _pickerUse = PickerUse.None;
                return;
            }

            if (_pickerUse == PickerUse.StocktakePick)
            {
                StocktakeResultsList.ItemsSource = new List<VariantRow> { v };
                _pickerUse = PickerUse.None;
                return;
            }

            _pickerUse = PickerUse.None;
        }

        // =========================
        // Stocktake (Actual Qty -> ADJUST difference)
        // =========================

        private void StocktakeSearch_Click(object sender, RoutedEventArgs e)
        {
            var q = (StocktakeQueryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q)) return;

            var results = ProductRepo.SearchVariantsByBarcodeOrName(q, limit: 100, includeInactive: true);
            if (results.Count == 0)
            {
                MessageBox.Show("مفيش نتائج.");
                return;
            }

            if (results.Count == 1)
            {
                StocktakeResultsList.ItemsSource = results;
                return;
            }

            _pickerCandidates = results;
            _pickerUse = PickerUse.StocktakePick;
            PickerHintText.Text = $"اختر الفاريانت للجرد: {q}";
            PickerList.ItemsSource = results;
            PickerOverlay.Visibility = Visibility.Visible;
        }

        private void ApplyStocktake_Click(object sender, RoutedEventArgs e)
        {
            if (StocktakeResultsList.SelectedItem is not VariantRow v)
            {
                MessageBox.Show("اختار Variant من النتائج.");
                return;
            }

            if (!decimal.TryParse((ActualQtyBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var actual))
            {
                MessageBox.Show("Actual Qty غير صحيح.");
                return;
            }

            var bid = SessionManager.CurrentBranchId;
            var current = StockRepo.GetStock(v.Id, bid);
            var diff = actual - current;

            if (diff == 0)
            {
                MessageBox.Show("مفيش فرق. المخزون مطابق.");
                return;
            }

            var notes = string.IsNullOrWhiteSpace(StocktakeNotesBox.Text) ? "STOCKTAKE" : StocktakeNotesBox.Text.Trim();

            SaveMovement(v.Id, diff, "ADJUST", null, notes);

            MessageBox.Show($"تم تسجيل الجرد. Current={current} Actual={actual} Diff={diff}");

            RefreshInventory();
            RefreshReport();
        }
    }
}