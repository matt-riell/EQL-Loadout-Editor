using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlLoadouts.App.Models;

namespace EqlLoadouts.App;

/// <summary>Crops shared window art. The spell-bar book is window_pieces01.tga at 181,61.</summary>
public static class UiPieces
{
    private static string? _directory;
    private static ImageSource? _book;

    public static ImageSource? Book(string? gameDirectory)
    {
        if (string.Equals(_directory, gameDirectory, StringComparison.OrdinalIgnoreCase) && _directory is not null)
        {
            return _book;
        }

        _directory = gameDirectory ?? string.Empty;
        _book = null;
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        var sheet = SpellIcons.LoadTga(Path.Combine(gameDirectory, "uifiles", "default", "window_pieces01.tga"));
        if (sheet is null || sheet.PixelWidth < 203 || sheet.PixelHeight < 83)
        {
            return null;
        }

        var crop = new CroppedBitmap(sheet, new Int32Rect(181, 61, 22, 22));
        crop.Freeze();
        _book = crop;
        return _book;
    }
}
