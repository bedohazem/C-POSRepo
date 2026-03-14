using POS_System.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace POS_System
{
    public partial class UsersPage : Page
    {
        private enum Mode { None, Add, Edit }
        private Mode _mode = Mode.None;

        private int? _editId = null;
        private List<UserRow> _all = new();
        private List<BranchPick> _branchPicks = new();

        private int? _resetUserId = null;

        public UsersPage()
        {
            InitializeComponent();

            // ✅ حماية إضافية (حتى لو MainWindow بيعمل Demand)
            AuthService.Demand(Permission.Users_Manage);

            Loaded += (_, _) =>
            {
                RoleBox.ItemsSource = UserRepo.GetRoles();

                _branchPicks = BranchRepo.GetBranches(includeInactive: false)
                    .Select(b => new BranchPick { Id = b.Id, Name = b.Name, IsSelected = false })
                    .ToList();

                BranchesList.ItemsSource = _branchPicks;

                LoadUsers();
            };
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadUsers();

        private void LoadUsers()
        {
            _all = UserRepo.GetUsers();
            ApplySearch();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

        private void ApplySearch()
        {
            var q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();

            IEnumerable<UserRow> src = _all;

            if (!string.IsNullOrWhiteSpace(q))
            {
                src = src.Where(x =>
                    (x.Username ?? "").ToLowerInvariant().Contains(q) ||
                    (x.RoleName ?? "").ToLowerInvariant().Contains(q) ||
                    (x.BranchName ?? "").ToLowerInvariant().Contains(q));
            }

            UsersList.ItemsSource = null;
            UsersList.ItemsSource = src.ToList();
        }

        private UserRow? SelectedRow()
            => UsersList.SelectedItem as UserRow;

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            _mode = Mode.Add;
            _editId = null;

            EditorTitle.Text = "Add User";
            HintText.Text = "Password is required for new user.";
            UserBox.Text = "";
            PassBox.Password = "";
            ActiveBox.IsChecked = true;

            RoleBox.SelectedIndex = 0;

            foreach (var b in _branchPicks) b.IsSelected = false;
            BranchesList.ItemsSource = null;
            BranchesList.ItemsSource = _branchPicks;

            EditorOverlay.Visibility = Visibility.Visible;
            UserBox.Focus();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            if (row == null)
            {
                MessageBox.Show("اختار مستخدم الأول.");
                return;
            }

            _mode = Mode.Edit;
            _editId = row.Id;

            EditorTitle.Text = "Edit User";
            HintText.Text = "Leave password empty to keep it unchanged.";
            PassBox.Password = "";

            LoadUser(_editId.Value);

            EditorOverlay.Visibility = Visibility.Visible;
            UserBox.Focus();
        }

        private void LoadUser(int id)
        {
            var u = UserRepo.GetUser(id);
            if (u == null) return;

            UserBox.Text = u.Value.username;
            ActiveBox.IsChecked = u.Value.isActive;

            RoleBox.SelectedItem = ((System.Collections.IEnumerable)RoleBox.ItemsSource)
                .Cast<LookupItem>().FirstOrDefault(x => x.Id == u.Value.roleId);

            var selectedIds = UserRepo.GetUserBranchIds(id);
            if (selectedIds.Count == 0)
                selectedIds.Add(u.Value.branchId);

            foreach (var b in _branchPicks)
                b.IsSelected = selectedIds.Contains(b.Id);

            BranchesList.ItemsSource = null;
            BranchesList.ItemsSource = _branchPicks;
        }

        private void ToggleActive_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            if (row == null)
            {
                MessageBox.Show("اختار مستخدم الأول.");
                return;
            }

            UserRepo.ToggleActive(row.Id);
            LoadUsers();
        }

        private void ResetPw_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            if (row == null)
            {
                MessageBox.Show("اختار مستخدم الأول.");
                return;
            }

            _resetUserId = row.Id;
            ResetUserInfo.Text = $"{row.Username} ({row.RoleName})";
            ResetPassBox.Password = "";
            ResetOverlay.Visibility = Visibility.Visible;
            ResetPassBox.Focus();
        }

        private void ResetCancel_Click(object sender, RoutedEventArgs e)
        {
            _resetUserId = null;
            ResetOverlay.Visibility = Visibility.Collapsed;
        }

        private void ResetSave_Click(object sender, RoutedEventArgs e)
        {
            if (_resetUserId == null)
            {
                ResetCancel_Click(sender, e);
                return;
            }

            var pw = (ResetPassBox.Password ?? "").Trim();
            if (string.IsNullOrWhiteSpace(pw))
            {
                MessageBox.Show("اكتب باسورد جديد.");
                return;
            }

            try
            {
                UserRepo.ResetPassword(_resetUserId.Value, pw);
                MessageBox.Show("Password updated.");
                ResetCancel_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            EditorOverlay.Visibility = Visibility.Collapsed;
            _mode = Mode.None;
            _editId = null;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var username = (UserBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Username required.");
                return;
            }

            if (RoleBox.SelectedItem is not LookupItem role)
            {
                MessageBox.Show("Select role.");
                return;
            }

            var selectedBranchIds = _branchPicks.Where(x => x.IsSelected).Select(x => x.Id).ToList();
            if (selectedBranchIds.Count == 0)
            {
                MessageBox.Show("Select at least one branch.");
                return;
            }

            var isActive = ActiveBox.IsChecked == true;
            var pw = (PassBox.Password ?? "").Trim();

            try
            {
                var primaryBranchId = selectedBranchIds[0];

                if (_mode == Mode.Edit && _editId.HasValue)
                {
                    UserRepo.UpdateUser(_editId.Value, username, role.Id, primaryBranchId, isActive);
                    UserRepo.SetUserBranches(_editId.Value, selectedBranchIds);

                    if (!string.IsNullOrWhiteSpace(pw))
                        UserRepo.ResetPassword(_editId.Value, pw);
                }
                else if (_mode == Mode.Add)
                {
                    if (string.IsNullOrWhiteSpace(pw))
                    {
                        MessageBox.Show("Password required for new user.");
                        return;
                    }

                    var newId = UserRepo.CreateUserReturnId(username, pw, role.Id, primaryBranchId, isActive);
                    UserRepo.SetUserBranches(newId, selectedBranchIds);
                }

                Cancel_Click(sender, e);
                LoadUsers();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        public class BranchPick
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsSelected { get; set; }
        }
    }
}