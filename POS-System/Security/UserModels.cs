using System.Collections.Generic;

namespace POS_System.Security
{
    public class Role
    {
        public string Name { get; set; } = "";
        public HashSet<Permission> Permissions { get; set; } = new();
    }

    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = ""; // مؤقتًا هنبسطها
        public Role Role { get; set; } = new Role();
        public bool IsActive { get; set; } = true;
        public string BranchName { get; set; } = "Main";

    }
}
