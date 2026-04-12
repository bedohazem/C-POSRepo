using Microsoft.Win32;
using POS_System.Localization;
using POS_System.printing;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;

namespace POS_System
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                DbPathBox.Text = Database.DbPath;
                BackupFolderBox.Text = Database.BackupFolder;
                BackupStatusText.Text = "جاهز لعمل نسخة احتياطية أو استرجاع.";

                LoadBarcodeSettings();
                BarcodeSettingsStatusText.Text = "Barcode settings loaded.";
                LoadPrinters();
                LoadPrinterSettings();
            };
        }

        private void LoadPrinters()
        {
            PrinterComboBox.Items.Clear();

            var printServer = new LocalPrintServer();
            var queues = printServer.GetPrintQueues();

            foreach (var q in queues)
                PrinterComboBox.Items.Add(q.Name);
        }
        private void LoadPrinterSettings()
        {
            var s = PrinterSettingsRepo.Get();

            PrinterComboBox.SelectedItem = s.PrinterName;

            foreach (ComboBoxItem item in PrintModeComboBox.Items)
            {
                if ((item.Content?.ToString() ?? "") == s.PrintMode)
                {
                    PrintModeComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        private void SavePrinterSettings_Click(object sender, RoutedEventArgs e)
        {
            var s = new PrinterSettings
            {
                PrinterName = PrinterComboBox.SelectedItem?.ToString(),
                PrintMode = (PrintModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Windows"
            };

            PrinterSettingsRepo.Save(s);

            MessageBox.Show("Printer settings saved.");
        }
        private void Print_Checked(object sender, RoutedEventArgs e) => PosSettings.PrintReceipt = true;
        private void Print_Unchecked(object sender, RoutedEventArgs e) => PosSettings.PrintReceipt = false;

        private void Dialog_Checked(object sender, RoutedEventArgs e) => PosSettings.ShowPrintDialog = true;
        private void Dialog_Unchecked(object sender, RoutedEventArgs e) => PosSettings.ShowPrintDialog = false;

        private void Arabic_Click(object sender, RoutedEventArgs e)
        {
            Loc.I.SetCulture("ar-EG");
        }

        private void English_Click(object sender, RoutedEventArgs e)
        {
            Loc.I.SetCulture("en-US");
        }

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Database.CreateBackup();
                BackupStatusText.Text = $"Backup created successfully:\n{path}";
                MessageBox.Show("Backup created successfully.", "Backup");
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Settings.BackupNow");
                MessageBox.Show("Backup failed:\n" + ex.Message, "Error");
            }
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select backup file",
                Filter = "Database Backup (*.db)|*.db|All Files (*.*)|*.*",
                InitialDirectory = Directory.Exists(Database.BackupFolder)
                    ? Database.BackupFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() != true)
                return;

            var confirm = MessageBox.Show(
                "استرجاع النسخة الاحتياطية هيستبدل قاعدة البيانات الحالية.\n\nهل أنت متأكد؟",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                Database.RestoreBackup(dlg.FileName);

                BackupStatusText.Text = $"Backup restored successfully:\n{dlg.FileName}";

                MessageBox.Show(
                    "تم استرجاع النسخة الاحتياطية بنجاح.\nهيتم إغلاق البرنامج الآن، افتحه مرة تانية.",
                    "Restore Complete");

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Settings.RestoreBackup");
                MessageBox.Show("Restore failed:\n" + ex.Message, "Error");
            }
        }

        private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Database.BackupFolder);

                Process.Start(new ProcessStartInfo
                {
                    FileName = Database.BackupFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Settings.OpenBackupFolder");
                MessageBox.Show("Could not open folder:\n" + ex.Message, "Error");
            }
        }
        private void LoadBarcodeSettings()
        {
            var s = BarcodePrintSettingsRepo.Get();

            BarcodeLabelWidthBox.Text = s.LabelWidthMm.ToString(CultureInfo.InvariantCulture);
            BarcodeLabelHeightBox.Text = s.LabelHeightMm.ToString(CultureInfo.InvariantCulture);

            BarcodeNameFontSizeBox.Text = s.NameFontSize.ToString(CultureInfo.InvariantCulture);
            BarcodePriceFontSizeBox.Text = s.PriceFontSize.ToString(CultureInfo.InvariantCulture);
            BarcodeTextFontSizeBox.Text = s.BarcodeTextFontSize.ToString(CultureInfo.InvariantCulture);

            BarcodeNameLeftBox.Text = s.NameLeftMm.ToString(CultureInfo.InvariantCulture);
            BarcodeNameTopBox.Text = s.NameTopMm.ToString(CultureInfo.InvariantCulture);

            BarcodePriceLeftBox.Text = s.PriceLeftMm.ToString(CultureInfo.InvariantCulture);
            BarcodePriceTopBox.Text = s.PriceTopMm.ToString(CultureInfo.InvariantCulture);

            BarcodeWidthBox.Text = s.BarcodeWidthMm.ToString(CultureInfo.InvariantCulture);
            BarcodeHeightBox.Text = s.BarcodeHeightMm.ToString(CultureInfo.InvariantCulture);
            BarcodeLeftBox.Text = s.BarcodeLeftMm.ToString(CultureInfo.InvariantCulture);
            BarcodeTopBox.Text = s.BarcodeTopMm.ToString(CultureInfo.InvariantCulture);

            BarcodeTextLeftBox.Text = s.BarcodeTextLeftMm.ToString(CultureInfo.InvariantCulture);
            BarcodeTextTopBox.Text = s.BarcodeTextTopMm.ToString(CultureInfo.InvariantCulture);

            ShowProductNameBox.IsChecked = s.ShowProductName;
            ShowPriceBox.IsChecked = s.ShowPrice;
            ShowBarcodeTextBox.IsChecked = s.ShowBarcodeText;
        }

        private BarcodePrintSettings ReadBarcodeSettingsFromUi()
        {
            double Parse(TextBox box, string fieldName)
            {
                if (!double.TryParse(box.Text?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    throw new Exception($"Invalid value in {fieldName}");
                return v;
            }

            return new BarcodePrintSettings
            {
                LabelWidthMm = Parse(BarcodeLabelWidthBox, "Label Width"),
                LabelHeightMm = Parse(BarcodeLabelHeightBox, "Label Height"),

                NameFontSize = Parse(BarcodeNameFontSizeBox, "Name Font Size"),
                PriceFontSize = Parse(BarcodePriceFontSizeBox, "Price Font Size"),
                BarcodeTextFontSize = Parse(BarcodeTextFontSizeBox, "Barcode Text Font Size"),

                NameLeftMm = Parse(BarcodeNameLeftBox, "Name Left"),
                NameTopMm = Parse(BarcodeNameTopBox, "Name Top"),

                PriceLeftMm = Parse(BarcodePriceLeftBox, "Price Left"),
                PriceTopMm = Parse(BarcodePriceTopBox, "Price Top"),

                BarcodeWidthMm = Parse(BarcodeWidthBox, "Barcode Width"),
                BarcodeHeightMm = Parse(BarcodeHeightBox, "Barcode Height"),
                BarcodeLeftMm = Parse(BarcodeLeftBox, "Barcode Left"),
                BarcodeTopMm = Parse(BarcodeTopBox, "Barcode Top"),

                BarcodeTextLeftMm = Parse(BarcodeTextLeftBox, "Barcode Text Left"),
                BarcodeTextTopMm = Parse(BarcodeTextTopBox, "Barcode Text Top"),

                ShowProductName = ShowProductNameBox.IsChecked == true,
                ShowPrice = ShowPriceBox.IsChecked == true,
                ShowBarcodeText = ShowBarcodeTextBox.IsChecked == true
            };
        }

        private void SaveBarcodeSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = ReadBarcodeSettingsFromUi();
                BarcodePrintSettingsRepo.Save(s);
                BarcodeSettingsStatusText.Text = "Barcode settings saved successfully.";
                MessageBox.Show("Barcode settings saved.", "Settings");
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Settings.SaveBarcodeSettings");
                MessageBox.Show(ex.Message, "Error");
            }
        }

        private void ResetBarcodeSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = new BarcodePrintSettings();
                BarcodePrintSettingsRepo.Save(s);
                LoadBarcodeSettings();
                BarcodeSettingsStatusText.Text = "Defaults restored.";
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Settings.ResetBarcodeSettings");
                MessageBox.Show(ex.Message, "Error");
            }
        }

        private void PrintTestBarcode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = ReadBarcodeSettingsFromUi();
                BarcodePrintSettingsRepo.Save(s);
                BarcodeSettingsStatusText.Text = "Saved. Go print from products page to test.";
                MessageBox.Show("Settings saved.\nروح صفحة المنتجات واطبع Print Barcode على أي variant للتجربة.", "Barcode Test");
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Settings.PrintTestBarcode");
                MessageBox.Show(ex.Message, "Error");
            }
        }
    }
}