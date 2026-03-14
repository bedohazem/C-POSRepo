using System;
using System.Globalization;
using System.Windows;

namespace POS_System
{
    public partial class CashPaymentWindow : Window
    {
        private readonly decimal _total;

        public decimal Received { get; private set; }
        public decimal Remains { get; private set; }
        public bool Confirmed { get; private set; }

        public CashPaymentWindow(decimal total)
        {
            InitializeComponent();
            _total = total;

            TotalText.Text = total.ToString("0.00");
            ChangeText.Text = "0.00";

            Loaded += (_, _) => ReceivedBox.Focus();
        }

        private void ReceivedBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // نقبل أرقام بنقطة أو فاصلة
            var text = (ReceivedBox.Text ?? "").Trim().Replace(',', '.');

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var received))
            {
                ConfirmBtn.IsEnabled = false;
                ChangeText.Text = "0.00";
                return;
            }

            Received = received;
            Remains = received - _total;

            ChangeText.Text = Remains.ToString("0.00");
            ConfirmBtn.IsEnabled = received >= _total && _total > 0;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }
    }
}
