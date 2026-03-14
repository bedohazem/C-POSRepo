using Microsoft.Win32;
using POS_System.Localization;
using System;
using System.Diagnostics;
using System.IO;
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
            };
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
    }
}