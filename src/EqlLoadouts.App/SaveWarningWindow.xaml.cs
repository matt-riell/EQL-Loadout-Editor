using System.Windows;

namespace EqlLoadouts.App;

public partial class SaveWarningWindow : Window
{
    public SaveWarningWindow()
    {
        InitializeComponent();
    }

    public bool DontShowAgain => DontShowBox.IsChecked == true;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
