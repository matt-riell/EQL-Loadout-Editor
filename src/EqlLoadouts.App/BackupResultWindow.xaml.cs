using System.IO;
using System.Windows;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.App;

public partial class BackupResultWindow : Window
{
    public BackupResultWindow(SaveResult result)
    {
        InitializeComponent();
        Heading.Text = result.Success ? "Backup completed" : "Backup failed";
        Detail.Text = result.Message;
        if (string.IsNullOrWhiteSpace(result.BackupDirectory))
        {
            Folder.Visibility = Visibility.Collapsed;
        }
        else
        {
            Folder.Text = result.BackupDirectory;
        }

        var root = result.BackupDirectory;
        foreach (var file in result.WrittenFiles)
        {
            Files.Items.Add(root is not null && file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(root, file)
                : file);
        }

        if (Files.Items.Count == 0)
        {
            FilesHeading.Visibility = Visibility.Collapsed;
            Files.Visibility = Visibility.Collapsed;
        }
        else
        {
            FilesHeading.Text = result.Success ? "Files copied" : "Files copied before the failure";
        }
    }
}
