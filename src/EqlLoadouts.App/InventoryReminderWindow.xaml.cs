using System.Windows;

namespace EqlLoadouts.App;

public partial class InventoryReminderWindow : Window
{
    private readonly string _command;

    public InventoryReminderWindow(string body, string command, bool dontShowAgain)
    {
        InitializeComponent();
        _command = command;
        BodyText.Text = body;
        CommandText.Text = command;
        DontShowBox.IsChecked = dontShowAgain;
    }

    public bool DontShowAgain => DontShowBox.IsChecked == true;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_command);
            CopyButton.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
