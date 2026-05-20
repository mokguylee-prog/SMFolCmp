using System.Windows;

namespace SMFolCmp.Views
{
    public partial class ConfirmDeleteDialog : Window
    {
        public bool IsConfirmed { get; private set; }

        public ConfirmDeleteDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
            IsConfirmed = false;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
