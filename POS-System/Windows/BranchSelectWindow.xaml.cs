using System.Collections.Generic;
using System.Windows;

namespace POS_System
{
    public partial class BranchSelectWindow : Window
    {
        public LookupItem? Selected { get; private set; }

        public BranchSelectWindow(List<LookupItem> branches)
        {
            InitializeComponent();
            List.ItemsSource = branches;
            if (branches.Count > 0) List.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Selected = List.SelectedItem as LookupItem;
            if (Selected == null) return;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
