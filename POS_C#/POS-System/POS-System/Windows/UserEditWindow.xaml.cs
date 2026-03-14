using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace POS_System
{
    public partial class UserEditWindow : Window
    {
        private readonly int? _editId;
        private List<BranchPick> _branchPicks = new();

        public UserEditWindow(int? editId = null)
        {
            InitializeComponent();
            _editId = editId;

            RoleBox.ItemsSource = UserRepo.GetRoles();

            // Active branches فقط (منع اختيار فرع مقفول)
            _branchPicks = BranchRepo.GetBranches(includeInactive: false)
                .Select(b => new BranchPick { Id = b.Id, Name = b.Name, IsSelected = false })
                .ToList();

            BranchesList.ItemsSource = _branchPicks;

            if (_editId.HasValue)
            {
                Title = "Edit User";
                HintText.Text = "Leave password empty to keep it unchanged.";
                PassBox.Password = "";
                LoadUser(_editId.Value);
            }
            else
            {
                Title = "Add User";
                HintText.Text = "Password is required for new user.";
                ActiveBox.IsChecked = true;
            }

            Loaded += (_, _) => UserBox.Focus();
        }

        private void LoadUser(int id)
        {
            var u = UserRepo.GetUser(id);
            if (u == null) return;

            UserBox.Text = u.Value.username;
            ActiveBox.IsChecked = u.Value.isActive;

            RoleBox.SelectedItem = ((System.Collections.IEnumerable)RoleBox.ItemsSource)
                .Cast<LookupItem>().FirstOrDefault(x => x.Id == u.Value.roleId);

            // Load user branches from UserBranches
            var selectedIds = UserRepo.GetUserBranchIds(id);

            // لو اليوزر القديم مش متسجل له فروع (احتياطي): اختار الـ primary branch بتاعه
            if (selectedIds.Count == 0)
                selectedIds.Add(u.Value.branchId);

            foreach (var b in _branchPicks)
                b.IsSelected = selectedIds.Contains(b.Id);

            // Refresh bindings
            BranchesList.ItemsSource = null;
            BranchesList.ItemsSource = _branchPicks;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var username = UserBox.Text.Trim();
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

            var selectedBranchIds = _branchPicks
                .Where(x => x.IsSelected)
                .Select(x => x.Id)
                .ToList();

            if (selectedBranchIds.Count == 0)
            {
                MessageBox.Show("Select at least one branch.");
                return;
            }

            var isActive = ActiveBox.IsChecked == true;
            var pw = PassBox.Password?.Trim() ?? "";

            try
            {
                // نخزن Primary branch في Users.BranchId للتوافق + نكتب باقي الفروع في UserBranches
                var primaryBranchId = selectedBranchIds[0];

                if (_editId.HasValue)
                {
                    UserRepo.UpdateUser(_editId.Value, username, role.Id, primaryBranchId, isActive);

                    // Multi-branches
                    UserRepo.SetUserBranches(_editId.Value, selectedBranchIds);

                    if (!string.IsNullOrWhiteSpace(pw))
                        UserRepo.ResetPassword(_editId.Value, pw);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(pw))
                    {
                        MessageBox.Show("Password required for new user.");
                        return;
                    }

                    var newId = UserRepo.CreateUserReturnId(username, pw, role.Id, primaryBranchId, isActive);

                    // Multi-branches
                    UserRepo.SetUserBranches(newId, selectedBranchIds);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public class BranchPick
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsSelected { get; set; }
        }
    }
}
