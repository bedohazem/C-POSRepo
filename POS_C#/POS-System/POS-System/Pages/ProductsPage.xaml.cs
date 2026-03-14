using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

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

            VarBarcodeBox.Text = "";
            VarSizeBox.Text = "";
            VarColorBox.Text = "";
            VarSellPriceBox.Text = "0";
            VarCostPriceBox.Text = "0";
            VarActiveBox.IsChecked = true;

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
                                throw new Exception("Barcode is required.");

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
