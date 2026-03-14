using POS_System.Security;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace POS_System
{
    // ✅ Local models for binding (no CustomerRepo needed)
    public class CustomerRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Notes { get; set; }

        public decimal LoyaltyPoints { get; set; }

        public string SpecialDiscountType { get; set; } = "None"; // None/Percent/Amount
        public decimal SpecialDiscountValue { get; set; }

        public bool IsActive { get; set; }
        public string CreatedAtUtc { get; set; } = "";
    }

    public class LoyaltyRow
    {
        public string AtUtc { get; set; } = "";
        public string Type { get; set; } = "";     // EARN / REDEEM / ADJUST
        public decimal Points { get; set; }
        public long? RefSaleId { get; set; }
        public string? Notes { get; set; }
        public string? Cashier { get; set; }
        public string? Branch { get; set; }
    }

    public class CustomerSaleRow
    {
        public long Id { get; set; }
        public string AtUtc { get; set; } = "";
        public decimal GrandTotal { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Notes { get; set; }
        public string? Cashier { get; set; }
        public string? Branch { get; set; }
    }

    public partial class CustomersPage : Page
    {
        private long _selectedCustomerId = 0;
        private long _editId = 0; // 0 = add

        public CustomersPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                RefreshCustomers();
                SetDetails(null);
            };
        }

        // =========================
        // DB Helpers
        // =========================

        private static SqliteConnection Open()
        {
            var con = new SqliteConnection(Database.ConnStr);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
            return con;
        }

        private static decimal ReadMoney(SqliteDataReader rd, int i)
        {
            if (rd.IsDBNull(i)) return 0m;
            return Convert.ToDecimal(rd.GetDouble(i), CultureInfo.InvariantCulture);
        }

        private static CustomerRow ReadCustomer(SqliteDataReader rd)
        {
            return new CustomerRow
            {
                Id = rd.GetInt64(0),
                Name = rd.GetString(1),
                Phone = rd.GetString(2),
                Email = rd.IsDBNull(3) ? null : rd.GetString(3),
                Address = rd.IsDBNull(4) ? null : rd.GetString(4),
                Notes = rd.IsDBNull(5) ? null : rd.GetString(5),
                LoyaltyPoints = ReadMoney(rd, 6),
                SpecialDiscountType = rd.IsDBNull(7) ? "None" : rd.GetString(7),
                SpecialDiscountValue = ReadMoney(rd, 8),
                IsActive = rd.GetInt32(9) == 1,
                CreatedAtUtc = rd.IsDBNull(10) ? "" : rd.GetString(10)
            };
        }

        // =========================
        // Queries
        // =========================

        private List<CustomerRow> SearchCustomers(string? q, bool includeInactive = true, int limit = 200)
        {
            var list = new List<CustomerRow>();
            q = (q ?? "").Trim();

            using var con = Open();
            using var cmd = con.CreateCommand();

            var activeFilter = includeInactive ? "" : "AND IsActive=1";

            if (string.IsNullOrWhiteSpace(q))
            {
                cmd.CommandText = $@"
SELECT Id, Name, Phone, Email, Address, Notes,
       LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue,
       IsActive, CreatedAtUtc
FROM Customers
WHERE 1=1 {activeFilter}
ORDER BY Id DESC
LIMIT {limit};";
            }
            else
            {
                cmd.CommandText = $@"
SELECT Id, Name, Phone, Email, Address, Notes,
       LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue,
       IsActive, CreatedAtUtc
FROM Customers
WHERE 1=1 {activeFilter}
  AND (Name LIKE @q OR Phone LIKE @q)
ORDER BY Id DESC
LIMIT {limit};";
                cmd.Parameters.AddWithValue("@q", "%" + q + "%");
            }

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                list.Add(ReadCustomer(rd));

            return list;
        }

        private CustomerRow? GetCustomerById(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Id, Name, Phone, Email, Address, Notes,
       LoyaltyPoints, SpecialDiscountType, SpecialDiscountValue,
       IsActive, CreatedAtUtc
FROM Customers
WHERE Id=@id
LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;
            return ReadCustomer(rd);
        }

        private long InsertCustomer(CustomerRow c)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO Customers(
  Name, Phone, Email, Address, Notes,
  LoyaltyPoints,
  SpecialDiscountType, SpecialDiscountValue,
  IsActive, CreatedAtUtc
)
VALUES(
  @n,@p,@e,@a,@no,
  @lp,
  @dt,@dv,
  @act,@at
);
SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@n", (c.Name ?? "").Trim());
            cmd.Parameters.AddWithValue("@p", (c.Phone ?? "").Trim());
            cmd.Parameters.AddWithValue("@e", string.IsNullOrWhiteSpace(c.Email) ? DBNull.Value : c.Email!.Trim());
            cmd.Parameters.AddWithValue("@a", string.IsNullOrWhiteSpace(c.Address) ? DBNull.Value : c.Address!.Trim());
            cmd.Parameters.AddWithValue("@no", string.IsNullOrWhiteSpace(c.Notes) ? DBNull.Value : c.Notes!.Trim());

            cmd.Parameters.AddWithValue("@lp", (double)c.LoyaltyPoints);
            cmd.Parameters.AddWithValue("@dt", string.IsNullOrWhiteSpace(c.SpecialDiscountType) ? "None" : c.SpecialDiscountType.Trim());
            cmd.Parameters.AddWithValue("@dv", (double)c.SpecialDiscountValue);

            cmd.Parameters.AddWithValue("@act", c.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));

            var idObj = cmd.ExecuteScalar();
            return (idObj == null || idObj == DBNull.Value) ? 0 : (long)idObj;
        }

        private void UpdateCustomer(long id, CustomerRow c)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE Customers
SET Name=@n, Phone=@p, Email=@e, Address=@a, Notes=@no,
    LoyaltyPoints=@lp,
    SpecialDiscountType=@dt, SpecialDiscountValue=@dv,
    IsActive=@act
WHERE Id=@id;";

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@n", (c.Name ?? "").Trim());
            cmd.Parameters.AddWithValue("@p", (c.Phone ?? "").Trim());
            cmd.Parameters.AddWithValue("@e", string.IsNullOrWhiteSpace(c.Email) ? DBNull.Value : c.Email!.Trim());
            cmd.Parameters.AddWithValue("@a", string.IsNullOrWhiteSpace(c.Address) ? DBNull.Value : c.Address!.Trim());
            cmd.Parameters.AddWithValue("@no", string.IsNullOrWhiteSpace(c.Notes) ? DBNull.Value : c.Notes!.Trim());

            cmd.Parameters.AddWithValue("@lp", (double)c.LoyaltyPoints);

            cmd.Parameters.AddWithValue("@dt", string.IsNullOrWhiteSpace(c.SpecialDiscountType) ? "None" : c.SpecialDiscountType.Trim());
            cmd.Parameters.AddWithValue("@dv", (double)c.SpecialDiscountValue);

            cmd.Parameters.AddWithValue("@act", c.IsActive ? 1 : 0);

            cmd.ExecuteNonQuery();
        }

        private void ToggleCustomerActive(long id)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Customers SET IsActive = CASE IsActive WHEN 1 THEN 0 ELSE 1 END WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        private void UpdateSpecialDiscount(long customerId, string type, decimal value)
        {
            type = (type ?? "None").Trim();
            if (type != "None" && type != "Percent" && type != "Amount")
                type = "None";

            if (type == "None") value = 0;
            if (value < 0) value = 0;

            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE Customers
SET SpecialDiscountType=@t, SpecialDiscountValue=@v
WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@v", (double)value);
            cmd.ExecuteNonQuery();
        }

        private List<LoyaltyRow> GetPointsHistory(long customerId, int limit = 200)
        {
            var list = new List<LoyaltyRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT lt.AtUtc, lt.Type, lt.Points, lt.RefSaleId, lt.Notes,
       u.Username AS Cashier, b.Name AS Branch
FROM LoyaltyTransactions lt
LEFT JOIN Users u ON u.Id = lt.UserId
LEFT JOIN Branches b ON b.Id = lt.BranchId
WHERE lt.CustomerId=@cid
ORDER BY lt.Id DESC
LIMIT {limit};";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new LoyaltyRow
                {
                    AtUtc = rd.IsDBNull(0) ? "" : rd.GetString(0),
                    Type = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    Points = ReadMoney(rd, 2),
                    RefSaleId = rd.IsDBNull(3) ? null : rd.GetInt64(3),
                    Notes = rd.IsDBNull(4) ? null : rd.GetString(4),
                    Cashier = rd.IsDBNull(5) ? null : rd.GetString(5),
                    Branch = rd.IsDBNull(6) ? null : rd.GetString(6),
                });
            }
            return list;
        }

        private List<CustomerSaleRow> GetSalesHistory(long customerId, int limit = 200)
        {
            var list = new List<CustomerSaleRow>();
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $@"
SELECT s.Id, s.AtUtc, s.GrandTotal, s.PaymentMethod, s.Type, s.Notes,
       u.Username AS Cashier, b.Name AS Branch
FROM Sales s
LEFT JOIN Users u ON u.Id = s.UserId
LEFT JOIN Branches b ON b.Id = s.BranchId
WHERE s.CustomerId=@cid
ORDER BY s.Id DESC
LIMIT {limit};";
            cmd.Parameters.AddWithValue("@cid", customerId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new CustomerSaleRow
                {
                    Id = rd.GetInt64(0),
                    AtUtc = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    GrandTotal = ReadMoney(rd, 2),
                    PaymentMethod = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    Type = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    Notes = rd.IsDBNull(5) ? null : rd.GetString(5),
                    Cashier = rd.IsDBNull(6) ? null : rd.GetString(6),
                    Branch = rd.IsDBNull(7) ? null : rd.GetString(7)
                });
            }
            return list;
        }

        private void AddPointsMovement(
            long customerId,
            decimal pointsDelta,
            string type,
            long? refSaleId,
            string? notes,
            int? userId,
            int? branchId)
        {
            if (pointsDelta == 0) return;
            type = (type ?? "ADJUST").Trim();

            using var con = Open();
            using var tx = con.BeginTransaction();

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO LoyaltyTransactions(CustomerId, Type, Points, RefSaleId, Notes, AtUtc, UserId, BranchId)
VALUES (@c,@t,@p,@ref,@n,@at,@u,@b);";
                cmd.Parameters.AddWithValue("@c", customerId);
                cmd.Parameters.AddWithValue("@t", type);
                cmd.Parameters.AddWithValue("@p", (double)pointsDelta);
                cmd.Parameters.AddWithValue("@ref", (object?)refSaleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes!.Trim());
                cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@u", (object?)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Customers SET LoyaltyPoints = LoyaltyPoints + @d WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@id", customerId);
                cmd.Parameters.AddWithValue("@d", (double)pointsDelta);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // =========================
        // UI glue
        // =========================

        private void RefreshCustomers()
        {
            CustomersList.ItemsSource = null;
            CustomersList.ItemsSource = SearchCustomers(SearchBox.Text, includeInactive: true);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCustomers();
        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCustomers();

        private void CustomersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomersList.SelectedItem is not CustomerRow c)
            {
                _selectedCustomerId = 0;
                SetDetails(null);
                return;
            }

            _selectedCustomerId = c.Id;
            SetDetails(GetCustomerById(c.Id));
        }

        private void SetDetails(CustomerRow? c)
        {
            if (c == null)
            {
                SelectedCustomerText.Text = "Select a customer...";
                DiscValueBox.Text = "0";
                DiscTypeBox.SelectedIndex = 0;
                DiscHint.Text = "";

                PointsSummaryText.Text = "Points: -";
                PointsList.ItemsSource = null;
                SalesList.ItemsSource = null;
                return;
            }

            SelectedCustomerText.Text = $"#{c.Id} - {c.Name}  |  {c.Phone}  |  Active: {c.IsActive}";
            PointsSummaryText.Text = $"Points: {c.LoyaltyPoints:0}";

            DiscValueBox.Text = c.SpecialDiscountValue.ToString("0.##", CultureInfo.InvariantCulture);

            var type = (c.SpecialDiscountType ?? "None").Trim();
            DiscTypeBox.SelectedIndex = type switch
            {
                "Percent" => 1,
                "Amount" => 2,
                _ => 0
            };

            DiscHint.Text = $"Rule: {type} {c.SpecialDiscountValue:0.##}";

            PointsList.ItemsSource = null;
            PointsList.ItemsSource = GetPointsHistory(c.Id);

            SalesList.ItemsSource = null;
            SalesList.ItemsSource = GetSalesHistory(c.Id);
        }

        // ===== CRUD UI =====

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            _editId = 0;
            EditorTitle.Text = "Add Customer";
            NameBox.Text = "";
            PhoneBox.Text = "";
            AddressBox.Text = "";
            // ✅ PointsBox removed: you said it doesn't exist in XAML
            ActiveBox.IsChecked = true;

            EditorOverlay.Visibility = Visibility.Visible;
            NameBox.Focus();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCustomerId == 0)
            {
                MessageBox.Show("اختار عميل الأول.");
                return;
            }

            var c = GetCustomerById(_selectedCustomerId);
            if (c == null) return;

            _editId = c.Id;
            EditorTitle.Text = "Edit Customer";
            NameBox.Text = c.Name;
            PhoneBox.Text = c.Phone;
            AddressBox.Text = c.Address ?? "";
            // ✅ PointsBox removed
            ActiveBox.IsChecked = c.IsActive;

            EditorOverlay.Visibility = Visibility.Visible;
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCustomerId == 0)
            {
                MessageBox.Show("اختار عميل الأول.");
                return;
            }

            ToggleCustomerActive(_selectedCustomerId);
            RefreshCustomers();
            SetDetails(GetCustomerById(_selectedCustomerId));
        }

        private void EditorCancel_Click(object sender, RoutedEventArgs e)
        {
            EditorOverlay.Visibility = Visibility.Collapsed;
            _editId = 0;
        }

        private void EditorSave_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Name required.");
                return;
            }

            var phone = (PhoneBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(phone))
            {
                MessageBox.Show("Phone required.");
                return;
            }

            // ✅ no PointsBox, keep points unchanged on edit; 0 on create
            var row = new CustomerRow
            {
                Id = _editId,
                Name = name,
                Phone = phone,
                Address = (AddressBox.Text ?? "").Trim(),
                LoyaltyPoints = 0m,
                IsActive = ActiveBox.IsChecked == true
            };

            try
            {
                if (_editId != 0)
                {
                    var old = GetCustomerById(_editId);
                    if (old != null)
                    {
                        row.LoyaltyPoints = old.LoyaltyPoints; // preserve
                        row.SpecialDiscountType = old.SpecialDiscountType;
                        row.SpecialDiscountValue = old.SpecialDiscountValue;
                        row.Email = old.Email;
                        row.Notes = old.Notes;
                    }

                    UpdateCustomer(_editId, row);
                    _selectedCustomerId = _editId;
                }
                else
                {
                    row.SpecialDiscountType = "None";
                    row.SpecialDiscountValue = 0;
                    var newId = InsertCustomer(row);
                    _selectedCustomerId = newId;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            EditorOverlay.Visibility = Visibility.Collapsed;
            _editId = 0;

            RefreshCustomers();
            SetDetails(GetCustomerById(_selectedCustomerId));
        }

        // ===== Discount Save =====

        private void SaveDiscount_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCustomerId == 0)
            {
                MessageBox.Show("اختار عميل الأول.");
                return;
            }

            var type = "None";
            if (DiscTypeBox.SelectedItem is ComboBoxItem it && it.Tag is string tag)
                type = tag;

            decimal.TryParse((DiscValueBox.Text ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val);
            if (val < 0) val = 0;
            if (type == "None") val = 0;

            try
            {
                UpdateSpecialDiscount(_selectedCustomerId, type, val);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            RefreshCustomers();
            SetDetails(GetCustomerById(_selectedCustomerId));
        }

        // ===== Points Adjust =====

        private void PointsAdjust_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCustomerId == 0)
            {
                MessageBox.Show("اختار عميل الأول.");
                return;
            }

            decimal.TryParse((PointsAdjustBox.Text ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var delta);
            if (delta == 0)
            {
                MessageBox.Show("اكتب + أو - نقاط.");
                return;
            }

            var notes = PointsNotesBox.Text;
            if (string.Equals(notes, "Notes (optional)", StringComparison.OrdinalIgnoreCase))
                notes = "";

            var u = SessionManager.CurrentUser;
            var b = SessionManager.CurrentBranchId;

            try
            {
                AddPointsMovement(
                    customerId: _selectedCustomerId,
                    pointsDelta: delta,
                    type: "ADJUST",
                    refSaleId: null,
                    notes: string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    userId: u?.Id,
                    branchId: b
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            PointsAdjustBox.Text = "0";
            PointsNotesBox.Text = "Notes (optional)";
            RefreshCustomers();
            SetDetails(GetCustomerById(_selectedCustomerId));
        }
    }
}