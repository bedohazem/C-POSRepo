using POS_System.printing;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Printing;
using System.Windows.Markup;


namespace POS_System
{
    public partial class ProductsPage : Page
    {
        private long? _selectedProductId;
        private long? _editingProductId;
        private long? _editingVariantId;

        private string? _pickedImagePath;

        private enum EditorMode { None, AddProduct, EditProduct, AddVariant, EditVariant }
        private EditorMode _mode = EditorMode.None;

        public ProductsPage()
        {
            InitializeComponent();
            RefreshProducts();
            UpdateRightHeader();
        }

        private string GenerateBarcode()
        {
            // رقم شبه EAN داخلي بسيط وفريد نسبيًا
            return DateTime.Now.ToString("yyMMddHHmmssfff");
        }

        private void RefreshBarcodePreview()
        {
            try
            {
                var text = (VarBarcodeBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(text))
                {
                    BarcodePreviewImage.Source = null;
                    return;
                }

                BarcodePreviewImage.Source = BarcodeHelper.GenerateCode128(text);
            }
            catch
            {
                BarcodePreviewImage.Source = null;
            }
        }

        private void VarBarcodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshBarcodePreview();
        }

        private void PrintBarcode_Click(object sender, RoutedEventArgs e)
        {
            if (VariantsList.SelectedItem is not VariantRow v)
            {
                MessageBox.Show("Select a variant first.");
                return;
            }

            int copies = 1;
            if (!int.TryParse((BarcodeCopiesBox.Text ?? "1").Trim(), out copies) || copies <= 0)
                copies = 1;

            var printerSettings = PrinterSettingsRepo.Get();

            // 🟢 TSPL (احترافي)
            if (printerSettings.PrintMode == "TSPL")
            {
                TsplPrinter.PrintBarcode(
                    printerSettings.PrinterName,
                    v.Barcode,
                    v.ProductName,
                    v.SellPrice,
                    copies);

                return;
            }

            // 🔵 Windows fallback (القديم)
            var s = BarcodePrintSettingsRepo.Get();

            var pd = new PrintDialog();
            if (pd.ShowDialog() != true)
                return;

            var doc = new FixedDocument();

            double pageWidth = MmToDip(s.LabelWidthMm);
            double pageHeight = MmToDip(s.LabelHeightMm);

            doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

            for (int i = 0; i < copies; i++)
            {
                var label = BuildBarcodeLabelElement(v, s);

                var pageContent = new PageContent();
                var fixedPage = new FixedPage
                {
                    Width = pageWidth,
                    Height = pageHeight,
                    Background = Brushes.White
                };

                fixedPage.Children.Add(label);
                ((IAddChild)pageContent).AddChild(fixedPage);
                doc.Pages.Add(pageContent);
            }

            pd.PrintDocument(doc.DocumentPaginator, "Barcode Labels");
        }

        private static double MmToDip(double mm)
        {
            return mm * 96.0 / 25.4;
        }

        private static FormattedText MakeText(string text, double fontSize, FontWeight weight)
        {
            return new FormattedText(
                text ?? "",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, weight, FontStretches.Normal),
                fontSize,
                Brushes.Black,
                1.0);
        }

        private static int MmToPixels(double mm, double dpi)
        {
            return (int)Math.Round(mm * dpi / 25.4);
        }

        private void DrawSingleLabel(DrawingContext dc, VariantRow v, BarcodePrintSettings s, double finalW, double finalH, double offsetY)
        {
            int barcodePxWidth = Math.Max(80, MmToPixels(s.BarcodeWidthMm, 203));
            int barcodePxHeight = Math.Max(20, MmToPixels(s.BarcodeHeightMm, 203));

            var barcode = BarcodeHelper.GenerateCode128(v.Barcode, barcodePxWidth, barcodePxHeight);

            // خلفية الليبل
            dc.DrawRectangle(Brushes.White, null, new Rect(0, offsetY, finalW, finalH));

            if (s.ShowProductName)
            {
                var name = MakeText(v.ProductName, s.NameFontSize, FontWeights.Bold);
                double x = MmToDip(s.NameLeftMm);
                double y = offsetY + MmToDip(s.NameTopMm);

                if (x < 0) x = 0;
                if (y < offsetY) y = offsetY;

                dc.DrawText(name, new Point(x, y));
            }

            if (s.ShowPrice)
            {
                var price = MakeText($"{v.SellPrice:0.##}", s.PriceFontSize, FontWeights.Bold);
                double x = MmToDip(s.PriceLeftMm);
                double y = offsetY + MmToDip(s.PriceTopMm);

                if (x < 0) x = 0;
                if (y < offsetY) y = offsetY;

                dc.DrawText(price, new Point(x, y));
            }

            double imgX = MmToDip(s.BarcodeLeftMm);
            double imgY = offsetY + MmToDip(s.BarcodeTopMm);
            double imgW = MmToDip(s.BarcodeWidthMm);
            double imgH = MmToDip(s.BarcodeHeightMm);

            if (imgX < 0) imgX = 0;
            if (imgY < offsetY) imgY = offsetY;

            if (imgX + imgW > finalW)
                imgW = Math.Max(20, finalW - imgX);

            dc.DrawImage(barcode, new Rect(imgX, imgY, imgW, imgH));

            if (s.ShowBarcodeText)
            {
                var bc = MakeText(v.Barcode, s.BarcodeTextFontSize, FontWeights.Regular);
                double x = MmToDip(s.BarcodeTextLeftMm);
                double y = offsetY + MmToDip(s.BarcodeTextTopMm);

                if (x < 0) x = 0;
                if (y < offsetY) y = offsetY;

                dc.DrawText(bc, new Point(x, y));
            }
        }

        private FrameworkElement BuildBarcodeLabelElement(VariantRow v, BarcodePrintSettings s)
        {
            double labelW = MmToDip(s.LabelWidthMm);
            double labelH = MmToDip(s.LabelHeightMm);

            int barcodePxWidth = Math.Max(80, MmToPixels(s.BarcodeWidthMm, 203));
            int barcodePxHeight = Math.Max(20, MmToPixels(s.BarcodeHeightMm, 203));

            var barcode = BarcodeHelper.GenerateCode128(v.Barcode, barcodePxWidth, barcodePxHeight);

            var canvas = new Canvas
            {
                Width = labelW,
                Height = labelH,
                Background = Brushes.White
            };

            if (s.ShowProductName)
            {
                var name = new TextBlock
                {
                    Text = v.ProductName,
                    FontSize = s.NameFontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                };

                Canvas.SetLeft(name, MmToDip(s.NameLeftMm));
                Canvas.SetTop(name, MmToDip(s.NameTopMm));
                canvas.Children.Add(name);
            }

            if (s.ShowPrice)
            {
                var price = new TextBlock
                {
                    Text = $"{v.SellPrice:0.##}",
                    FontSize = s.PriceFontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                };

                Canvas.SetLeft(price, MmToDip(s.PriceLeftMm));
                Canvas.SetTop(price, MmToDip(s.PriceTopMm));
                canvas.Children.Add(price);
            }

            var img = new Image
            {
                Source = barcode,
                Width = MmToDip(s.BarcodeWidthMm),
                Height = MmToDip(s.BarcodeHeightMm),
                Stretch = Stretch.Fill
            };

            Canvas.SetLeft(img, MmToDip(s.BarcodeLeftMm));
            Canvas.SetTop(img, MmToDip(s.BarcodeTopMm));
            canvas.Children.Add(img);

            if (s.ShowBarcodeText)
            {
                var bc = new TextBlock
                {
                    Text = v.Barcode,
                    FontSize = s.BarcodeTextFontSize,
                    Foreground = Brushes.Black
                };

                Canvas.SetLeft(bc, MmToDip(s.BarcodeTextLeftMm));
                Canvas.SetTop(bc, MmToDip(s.BarcodeTextTopMm));
                canvas.Children.Add(bc);
            }

            return canvas;
        }





        // --------- Refresh ---------

        private void RefreshProducts()
        {
            ProductsList.ItemsSource = null;
            ProductsList.ItemsSource = ProductRepo.GetProducts(ProductSearchBox.Text, includeInactive: true);
        }

        private void RefreshVariants()
        {
            VariantsList.ItemsSource = null;
            if (_selectedProductId == null) return;

            VariantsList.ItemsSource = ProductRepo.GetVariantsByProduct(_selectedProductId.Value, includeInactive: true);
        }

        private void UpdateRightHeader()
        {
            if (_selectedProductId == null)
            {
                SelectedProductText.Text = "Select a product to manage its variants.";
                return;
            }

            var p = ProductsList.SelectedItem as ProductRow;
            SelectedProductText.Text = p == null
                ? $"Selected ProductId: {_selectedProductId}"
                : $"Product: {p.Name} (Id: {p.Id})";
        }

        // --------- Product List ---------

        private void ProductSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshProducts();

        private void ProductsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProductsList.SelectedItem is ProductRow p)
            {
                _selectedProductId = p.Id;
                UpdateRightHeader();
                RefreshVariants();
            }
            else
            {
                _selectedProductId = null;
                UpdateRightHeader();
                VariantsList.ItemsSource = null;
            }
        }

        // --------- Product CRUD ---------

        private void AddProduct_Click(object sender, RoutedEventArgs e)
        {
            _mode = EditorMode.AddProduct;
            _editingProductId = null;
            _pickedImagePath = null;

            EditorTitle.Text = "Add Product";
            ProductEditor.Visibility = Visibility.Visible;
            VariantEditor.Visibility = Visibility.Collapsed;

            ProdNameBox.Text = "";
            ProdActiveBox.IsChecked = true;

            // ✅ جديد
            ProdThresholdBox.Text = "5";
            ProdImagePathBox.Text = "";

            ShowEditor();
        }

        private void EditProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not ProductRow p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            _mode = EditorMode.EditProduct;
            _editingProductId = p.Id;
            _pickedImagePath = p.ImagePath;

            EditorTitle.Text = "Edit Product";
            ProductEditor.Visibility = Visibility.Visible;
            VariantEditor.Visibility = Visibility.Collapsed;

            ProdNameBox.Text = p.Name;
            ProdActiveBox.IsChecked = p.IsActive;

            // ✅ جديد
            ProdThresholdBox.Text = p.LowStockThreshold.ToString(CultureInfo.InvariantCulture);
            ProdImagePathBox.Text = p.ImagePath ?? "";

            ShowEditor();
        }

        private void ToggleProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not ProductRow p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            ProductRepo.ToggleProductActive(p.Id);
            RefreshProducts();
        }

        private void DeleteProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not ProductRow p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            if (MessageBox.Show($"Delete product '{p.Name}'?\nAll variants will be deleted too.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            ProductRepo.DeleteProduct(p.Id);

            _selectedProductId = null;
            RefreshProducts();
            VariantsList.ItemsSource = null;
            UpdateRightHeader();
        }

        // ✅ اختيار صورة المنتج
        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
            };

            if (dlg.ShowDialog() == true)
            {
                _pickedImagePath = dlg.FileName;
                ProdImagePathBox.Text = _pickedImagePath;
            }
        }

        // --------- Variants ---------

        private void GenerateBarcode_Click(object sender, RoutedEventArgs e)
        {
            VarBarcodeBox.Text = GenerateBarcode();
            RefreshBarcodePreview();
        }
        private void AddVariant_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProductId == null)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            _mode = EditorMode.AddVariant;
            _editingVariantId = null;

            EditorTitle.Text = "Add Variant";
            ProductEditor.Visibility = Visibility.Collapsed;
            VariantEditor.Visibility = Visibility.Visible;

            VarBarcodeBox.Text = GenerateBarcode();
            VarSizeBox.Text = "";
            VarColorBox.Text = "";
            VarSellPriceBox.Text = "0";
            VarCostPriceBox.Text = "0";
            VarActiveBox.IsChecked = true;
           
            RefreshBarcodePreview();
            ShowEditor();
        }

        private void EditVariant_Click(object sender, RoutedEventArgs e)
        {
            if (VariantsList.SelectedItem is not VariantRow v)
            {
                MessageBox.Show("Select a variant first.");
                return;
            }

            _mode = EditorMode.EditVariant;
            _editingVariantId = v.Id;

            EditorTitle.Text = "Edit Variant";
            ProductEditor.Visibility = Visibility.Collapsed;
            VariantEditor.Visibility = Visibility.Visible;

            VarBarcodeBox.Text = v.Barcode;
            VarSizeBox.Text = v.Size;
            VarColorBox.Text = v.Color;

            VarSellPriceBox.Text = v.SellPrice.ToString("0.00", CultureInfo.InvariantCulture);
            VarCostPriceBox.Text = v.CostPrice.ToString("0.00", CultureInfo.InvariantCulture);

            VarActiveBox.IsChecked = v.IsActive;

            RefreshBarcodePreview();
            ShowEditor();
        }

        private void ToggleVariant_Click(object sender, RoutedEventArgs e)
        {
            if (VariantsList.SelectedItem is not VariantRow v)
            {
                MessageBox.Show("Select a variant first.");
                return;
            }

            ProductRepo.ToggleVariantActive(v.Id);
            RefreshVariants();
        }

        private void DeleteVariant_Click(object sender, RoutedEventArgs e)
        {
            if (VariantsList.SelectedItem is not VariantRow v)
            {
                MessageBox.Show("Select a variant first.");
                return;
            }

            if (MessageBox.Show($"Delete variant barcode '{v.Barcode}'?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            ProductRepo.DeleteVariant(v.Id);
            RefreshVariants();
        }

        // --------- Editor Overlay ---------

        private void ShowEditor() => EditorOverlay.Visibility = Visibility.Visible;

        private void HideEditor()
        {
            EditorOverlay.Visibility = Visibility.Collapsed;
            _mode = EditorMode.None;
            _editingProductId = null;
            _editingVariantId = null;
            _pickedImagePath = null;
        }

        private void EditorCancel_Click(object sender, RoutedEventArgs e) => HideEditor();

        private void EditorSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                switch (_mode)
                {
                    case EditorMode.AddProduct:
                        {
                            var name = (ProdNameBox.Text ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(name))
                                throw new Exception("Product name is required.");

                            if (!int.TryParse((ProdThresholdBox.Text ?? "5").Trim(), out var th))
                                throw new Exception("Invalid low stock threshold.");

                            var active = ProdActiveBox.IsChecked == true;
                            var img = string.IsNullOrWhiteSpace(ProdImagePathBox.Text) ? null : ProdImagePathBox.Text.Trim();

                            var id = ProductRepo.CreateProduct(name, img, th, active);

                            RefreshProducts();
                            _selectedProductId = id;

                            // select the newly added product in UI
                            foreach (var it in ProductsList.Items)
                            {
                                if (it is ProductRow pr && pr.Id == id)
                                {
                                    ProductsList.SelectedItem = it;
                                    ProductsList.ScrollIntoView(it);
                                    break;
                                }
                            }
                            UpdateRightHeader();
                            RefreshVariants();

                            HideEditor();
                            return;
                        }

                    case EditorMode.EditProduct:
                        {
                            if (_editingProductId == null) return;

                            var name = (ProdNameBox.Text ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(name))
                                throw new Exception("Product name is required.");

                            if (!int.TryParse((ProdThresholdBox.Text ?? "5").Trim(), out var th))
                                throw new Exception("Invalid low stock threshold.");

                            var active = ProdActiveBox.IsChecked == true;
                            var img = string.IsNullOrWhiteSpace(ProdImagePathBox.Text) ? null : ProdImagePathBox.Text.Trim();

                            ProductRepo.UpdateProduct(_editingProductId.Value, name, img, th, active);

                            RefreshProducts();
                            HideEditor();
                            return;
                        }

                    case EditorMode.AddVariant:
                        {
                            if (_selectedProductId == null) throw new Exception("Select product first.");

                            var bc = (VarBarcodeBox.Text ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(bc))
                                bc = GenerateBarcode();

                            var size = (VarSizeBox.Text ?? "").Trim();
                            var color = (VarColorBox.Text ?? "").Trim();

                            if (!decimal.TryParse((VarSellPriceBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sellPrice))
                                throw new Exception("Invalid sell price.");

                            if (!decimal.TryParse((VarCostPriceBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var costPrice))
                                throw new Exception("Invalid cost price.");

                            var active = VarActiveBox.IsChecked == true;

                            ProductRepo.CreateVariant(
                                productId: _selectedProductId.Value,
                                barcode: bc,
                                size: size,
                                color: color,
                                sellPrice: sellPrice,
                                costPrice: costPrice,
                                isActive: active);

                            RefreshVariants();
                            HideEditor();
                            return;
                        }

                    case EditorMode.EditVariant:
                        {
                            if (_editingVariantId == null) return;

                            var bc = (VarBarcodeBox.Text ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(bc))
                                throw new Exception("Barcode is required.");

                            var size = (VarSizeBox.Text ?? "").Trim();
                            var color = (VarColorBox.Text ?? "").Trim();

                            if (!decimal.TryParse((VarSellPriceBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sellPrice))
                                throw new Exception("Invalid sell price.");

                            if (!decimal.TryParse((VarCostPriceBox.Text ?? "0").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var costPrice))
                                throw new Exception("Invalid cost price.");

                            var active = VarActiveBox.IsChecked == true;

                            ProductRepo.UpdateVariant(
                                id: _editingVariantId.Value,
                                barcode: bc,
                                size: size,
                                color: color,
                                sellPrice: sellPrice,
                                costPrice: costPrice,
                                isActive: active);

                            RefreshVariants();
                            HideEditor();
                            return;
                        }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }
        private void ProductsList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // ثابتين
            const double idW = 70;
            const double thW = 90;
            const double activeW = 90;

            // مساحة إضافية تقديرية (scrollbar + padding + borders)
            const double extra = 40;

            // عرض الـ ListView
            var total = ProductsList.ActualWidth;

            // اسم المنتج ياخد الباقي
            var nameW = total - (idW + thW + activeW + extra);
            if (nameW < 120) nameW = 120; // حد أدنى

            ColId.Width = idW;
            ColTh.Width = thW;
            ColActive.Width = activeW;
            ColName.Width = nameW;
        }
    }
}
