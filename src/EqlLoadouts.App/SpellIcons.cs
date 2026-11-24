using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlLoadouts.Core.Content;

namespace EqlLoadouts.App.Models;

/// <summary>Crops spell icons out of the game's gemicons##.tga sheets.</summary>
public sealed class SpellIcons
{
    private readonly string? _folder;
    private readonly Dictionary<int, BitmapSource> _sheets = [];
    private readonly Dictionary<int, ImageSource?> _icons = [];

    private SpellIcons(string? folder)
    {
        _folder = folder;
    }

    public static SpellIcons Empty { get; } = new(null);

    public static SpellIcons Load(string? gameDirectory, int set)
    {
        var folder = FindFolder(gameDirectory, set);
        return folder is null ? Empty : new SpellIcons(folder);
    }

    public ImageSource? For(int icon)
    {
        if (!SpellIconLayout.TryLocate(icon, out var sheetNumber, out var column, out var row))
        {
            return null;
        }

        if (_icons.TryGetValue(icon, out var cached))
        {
            return cached;
        }

        var sheet = Sheet(sheetNumber);
        if (sheet is null)
        {
            _icons[icon] = null;
            return null;
        }

        var size = SpellIconLayout.Size;
        if ((column + 1) * size > sheet.PixelWidth || (row + 1) * size > sheet.PixelHeight)
        {
            _icons[icon] = null;
            return null;
        }

        var crop = new CroppedBitmap(sheet, new System.Windows.Int32Rect(
            column * size,
            row * size,
            size,
            size));
        crop.Freeze();
        _icons[icon] = crop;
        return crop;
    }

    private BitmapSource? Sheet(int sheetNumber)
    {
        if (_folder is null)
        {
            return null;
        }

        if (_sheets.TryGetValue(sheetNumber, out var sheet))
        {
            return sheet;
        }

        var path = Path.Combine(_folder, $"gemicons{sheetNumber:00}.tga");
        sheet = LoadSheet(path);
        if (sheet is not null)
        {
            _sheets[sheetNumber] = sheet;
        }

        return sheet;
    }

    internal static BitmapSource? LoadTga(string path) => LoadSheet(path);

    private static BitmapSource? LoadSheet(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (!TgaReader.TryRead(bytes, out var width, out var height, out var pixels))
        {
            return null;
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static string? FindFolder(string? gameDirectory, int set)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        var chosen = set switch
        {
            0 => Path.Combine(gameDirectory, "uifiles", "default"),
            >= 1 and <= 3 => Path.Combine(gameDirectory, "Textures", $"Alternate {set}"),
            _ => Path.Combine(gameDirectory, "uifiles", "default"),
        };
        foreach (var folder in new[] { chosen, Path.Combine(gameDirectory, "uifiles", "default") })
        {
            if (File.Exists(Path.Combine(folder, "gemicons01.tga")))
            {
                return folder;
            }
        }

        return null;
    }
}
