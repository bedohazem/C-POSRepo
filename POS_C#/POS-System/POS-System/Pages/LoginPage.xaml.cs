using System.Windows;
using POS_System.Audit;
using POS_System.Security;

namespace POS_System.Windows
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => UserBox.Focus();
        }


        private void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";

            var username = UserBox.Text.Trim();
            var u = AuthService.Login(username, PassBox.Password);

            if (u == null)
            {
                ErrorText.Text = "اسم المستخدم أو كلمة المرور غير صحيحة.";
                AuditLog.Write("LOGIN_FAILED", $"username={username}");
                return;
            }

            var branches = AuthService.GetUserBranches(u.Id);

            if (branches.Count == 0)
            {
                ErrorText.Text = "لا يوجد فرع نشط مرتبط بهذا المستخدم.";
                AuditLog.Write("LOGIN_BLOCKED_NO_ACTIVE_BRANCH", $"userId={u.Id}");
                return;
            }

            // مؤقتًا: أول فرع
            var b = branches[0];

            SessionManager.SignIn(u, b.Id, b.Name);
            AuditLog.Write("LOGIN_SUCCESS", $"user={u.Username}, branch={b.Name}");

            DialogResult = true;
            Close();
        }
    }
}
