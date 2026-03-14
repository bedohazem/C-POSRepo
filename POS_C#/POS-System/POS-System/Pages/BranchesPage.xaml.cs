using POS_System.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace POS_System
{
    public partial class BranchesPage : Page
    {
        private enum Mode { None, Add, Edit }
        private Mode _mode = Mode.None;

        private int? _editId = null;
        private List<BranchRow> _all = new();

        public BranchesPage()
        {
            InitializeComponent();

            // ✅ حماية إضافية
            AuthService.Demand(Permission.Users_Manage);

            Loaded += (_, _) => Load();
        }

        private void Load()
        {
            _all = BranchRepo.GetBranches(includeInactive: true);
            ApplySearch();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

        private void ApplySearch()
        {
            var q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();

            IEnumerable<BranchRow> src = _all;

            if (!string.IsNullOrWhiteSpace(q))
                src = src.Where(x => (x.Name ?? "").ToLowerInvariant().Contains(q));

            BranchesList.ItemsSource = null;
            BranchesList.ItemsSource = src.ToList();
        }

        private BranchRow? SelectedRow() => BranchesList.SelectedItem as BranchRow;

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            _mode = Mode.Add;
            _editId = null;

            EditorTitle.Text = "Add Branch";
            NameBox.Text = "";
            ActiveBox.IsChecked = true;

            EditorOverlay.Visibility = Visibility.Visible;
            NameBox.Focus();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            if (row == null)
            {
                MessageBox.Show("اختار فرع الأول.");
                return;
            }

            _mode = Mode.Edit;
            _editId = row.Id;

            EditorTitle.Text = "Edit Branch";
            NameBox.Text = row.Name;
            ActiveBox.IsChecked = row.IsActive;

            EditorOverlay.Visibility = Visibility.Visible;
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            if (row == null)
            {
                MessageBox.Show("اختار فرع الأول.");
                return;
            }

            // ✅ منع تعطيل الفرع الأساسي Main (اختياري)
            // لو مش عايزه احذف الشرط ده
            if (string.Equals(row.Name, "Main", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("مينفعش تعطّل فرع Main.");
                return;
            }

            BranchRepo.ToggleActive(row.Id);
            Load();
        }

        private void EditorCancel_Click(object sender, RoutedEventArgs e)
        {
            EditorOverlay.Visibility = Visibility.Collapsed;
            _mode = Mode.None;
            _editId = null;
        }

        private void EditorSave_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Branch name required.");
                return;
            }

            var active = ActiveBox.IsChecked == true;

            try
            {
                if (_mode == Mode.Edit && _editId.HasValue)
                    BranchRepo.Update(_editId.Value, name, active);
                else if (_mode == Mode.Add)
                    BranchRepo.Create(name, active);

                EditorCancel_Click(sender, e);
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}