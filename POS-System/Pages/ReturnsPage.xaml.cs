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
    public partial class ReturnsPage : Page
    {
        private SalesRepo.SaleHeader? _sale;
        private List<SalesRepo.SaleItemRow> _saleItems = new();

        private class ReturnLine
        {
            public long VariantId { get; set; }
            public string Barcode { get; set; } = "";
            public string Size { get; set; } = "";
            public string Color { get; set; } = "";
            public decimal UnitPrice { get; set; }
            public decimal UnitCost { get; set; }
            public int Qty { get; set; }

            public decimal LineTotalAfterDiscount => UnitPrice * Qty;
        }

        private readonly List<ReturnLine> _returnCart = new();

        private SalesRepo.SaleItemRow? _pendingItemForQty = null;

        public ReturnsPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                ClearAll();
            };
        }

        private void SaleIdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Search();
        }

        private void Search_Click(object sender, RoutedEventArgs e) => Search();
        private void Clear_Click(object sender, RoutedEventArgs e) => ClearAll();

        private void Search()
        {
            if (!long.TryParse(SaleIdBox.Text.Trim(), out var saleId) || saleId <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var h = SalesRepo.GetSaleHeader(saleId);
            if (h == null)
            {
                MessageBox.Show("الفاتورة غير موجودة.");
                return;
            }

            if (!string.Equals(h.Type, "Sale", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("لازم تختار فاتورة بيع (Sale) مش Return.");
                return;
            }

            _sale = h;
            _saleItems = SalesRepo.GetSaleItemsWithReturned(saleId);

            SaleInfoText.Text = $"فاتورة #{_sale.Id} | {(_sale.AtUtc.Length >= 10 ? _sale.AtUtc.Substring(0, 10) : _sale.AtUtc)} | Method: {_sale.PaymentMethod}";
            SaleItemsList.ItemsSource = null;
            SaleItemsList.ItemsSource = _saleItems;

            // فضي سلة المرتجع عند البحث الجديد
            _returnCart.Clear();
            RefreshReturnCart();
        }

        private void AddReturnLine_Click(object sender, RoutedEventArgs e)
        {
            if (_sale == null)
            {
                MessageBox.Show("اختار الفاتورة الأول.");
                return;
            }

            if (SaleItemsList.SelectedItem is not SalesRepo.SaleItemRow it)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            if (it.MaxReturnQty <= 0)
            {
                MessageBox.Show("تم إرجاع كل الكمية لهذا الصنف بالفعل.");
                return;
            }

            _pendingItemForQty = it;
            QtyHintText.Text = $"Max return = {it.MaxReturnQty} للباركود {it.Barcode}";
            QtyBox.Text = "1";
            QtyOverlay.Visibility = Visibility.Visible;
            QtyBox.Focus();
            QtyBox.SelectAll();
        }

        private void QtyOk_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingItemForQty == null) { QtyOverlay.Visibility = Visibility.Collapsed; return; }

            if (!int.TryParse(QtyBox.Text.Trim(), out var q) || q <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            if (q > _pendingItemForQty.MaxReturnQty)
            {
                MessageBox.Show($"الكمية أكبر من المسموح. Max = {_pendingItemForQty.MaxReturnQty}");
                return;
            }

            // لو نفس الـ Variant موجود في السلة، زوّد مع مراعاة الحد
            var existing = _returnCart.FirstOrDefault(x => x.VariantId == _pendingItemForQty.VariantId);
            var alreadyInCart = existing?.Qty ?? 0;

            if (alreadyInCart + q > _pendingItemForQty.MaxReturnQty)
            {
                MessageBox.Show($"إجمالي المرتجع لهذا الصنف في السلة هيعدي الحد. Max = {_pendingItemForQty.MaxReturnQty}");
                return;
            }

            if (existing == null)
            {
                _returnCart.Add(new ReturnLine
                {
                    VariantId = _pendingItemForQty.VariantId,
                    Barcode = _pendingItemForQty.Barcode,
                    Size = _pendingItemForQty.Size,
                    Color = _pendingItemForQty.Color,
                    UnitPrice = _pendingItemForQty.UnitPrice,
                    UnitCost = _pendingItemForQty.UnitCost,
                    Qty = q
                });
            }
            else
            {
                existing.Qty += q;
            }

            QtyOverlay.Visibility = Visibility.Collapsed;
            _pendingItemForQty = null;
            RefreshReturnCart();
        }

        private void QtyCancel_Click(object sender, RoutedEventArgs e)
        {
            QtyOverlay.Visibility = Visibility.Collapsed;
            _pendingItemForQty = null;
        }

        private void RemoveReturnLine_Click(object sender, RoutedEventArgs e)
        {
            if (ReturnCartList.SelectedItem is ReturnLine rl)
            {
                _returnCart.Remove(rl);
                RefreshReturnCart();
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
            }
        }

        private void ClearReturnCart_Click(object sender, RoutedEventArgs e)
        {
            _returnCart.Clear();
            RefreshReturnCart();
        }

        private void RefreshReturnCart()
        {
            ReturnCartList.ItemsSource = null;
            ReturnCartList.ItemsSource = _returnCart;

            var total = _returnCart.Sum(x => x.LineTotalAfterDiscount);
            ReturnTotalText.Text = $"Total: {total:0.00}";
        }

        private void ConfirmReturn_Click(object sender, RoutedEventArgs e)
        {
            if (_sale == null)
            {
                MessageBox.Show("اختار الفاتورة الأول.");
                return;
            }

            if (_returnCart.Count == 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var u = SessionManager.CurrentUser;
            var branchId = SessionManager.CurrentBranchId;
            if (u == null || branchId == null)
            {
                MessageBox.Show("Session missing user/branch.");
                return;
            }

            // ✅ رجّع بنفس بيانات الفاتورة (CustomerId + RefSaleId)
            var returnTotal = _returnCart.Sum(x => x.LineTotalAfterDiscount);

            var lines = _returnCart.Select(x => new SalesRepo.SaleLine(
                VariantId: x.VariantId,
                Name: $"Variant {x.Barcode}",
                Barcode: x.Barcode,
                Size: x.Size,
                Color: x.Color,
                Qty: x.Qty,
                UnitPrice: x.UnitPrice,
                LineDiscountType: "None",
                LineDiscountValue: 0m,
                LineTotalAfterDiscount: x.LineTotalAfterDiscount
            )).ToList();

            try
            {
                var returnId = SalesRepo.CreateSaleV2(
                    userId: u.Id,
                    branchId: branchId.Value,
                    type: "Return",
                    customerId: _sale.CustomerId,            // ✅ نفس العميل
                    subTotal: returnTotal,
                    invoiceDiscountType: "None",
                    invoiceDiscountValue: 0m,
                    grandTotal: returnTotal,
                    paid: 0m,                                // ✅ تبسيط: مش هنحسب دفع هنا
                    change: 0m,
                    method: _sale.PaymentMethod,             // ✅ نفس طريقة الدفع (أو "CashOut" لو تحب)
                    refSaleId: _sale.Id,                     // ✅ ربط المرتجع بالفاتورة
                    notes: "Return",
                    items: lines
                );

                MessageBox.Show($"تم إنشاء مرتجع #{returnId} على فاتورة #{_sale.Id}", "Return");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Return failed:\n" + ex.Message);
                return;
            }

            // اعمل refresh للفاتورة عشان AlreadyReturnedQty تتحدث
            Search();
        }

        private void ClearAll()
        {
            _sale = null;
            _saleItems.Clear();
            _returnCart.Clear();

            SaleInfoText.Text = "ابحث برقم الفاتورة";
            SaleItemsList.ItemsSource = null;
            RefreshReturnCart();
        }
    }
}