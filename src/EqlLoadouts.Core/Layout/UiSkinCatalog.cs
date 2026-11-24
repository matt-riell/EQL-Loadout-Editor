using System.Globalization;
using System.Xml.Linq;

namespace EqlLoadouts.Core.Layout;

public sealed record WindowChrome(double? Width, double? Height, bool Sizable);

public sealed class UiSkinCatalog
{
    private readonly Dictionary<string, WindowChrome> _screens;

    private UiSkinCatalog(Dictionary<string, WindowChrome> screens)
    {
        _screens = screens;
    }

    public static UiSkinCatalog Empty { get; } = new(new Dictionary<string, WindowChrome>(StringComparer.Ordinal));

    public WindowChrome? Find(string screenName)
    {
        return _screens.TryGetValue(screenName, out var chrome) ? chrome : null;
    }

    public static UiSkinCatalog Load(string gameDirectory, string? skin)
    {
        if (string.IsNullOrWhiteSpace(skin))
        {
            return Empty;
        }

        var directory = Path.Combine(gameDirectory, "uifiles", skin);
        if (!Directory.Exists(directory))
        {
            return Empty;
        }

        var screens = new Dictionary<string, WindowChrome>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "EQUI_*.xml", SearchOption.TopDirectoryOnly))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(path, LoadOptions.None);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var screen in document.Descendants().Where(element => element.Name.LocalName == "Screen"))
            {
                var item = screen.Attribute("item")?.Value;
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                var chrome = ReadChrome(screen);
                if (screens.TryGetValue(item, out var existing) && existing.Width is not null)
                {
                    continue;
                }

                screens[item] = chrome;
            }
        }

        return new UiSkinCatalog(screens);
    }

    private static WindowChrome ReadChrome(XElement screen)
    {
        double? width = null;
        double? height = null;
        var size = screen.Elements().FirstOrDefault(element => element.Name.LocalName == "Size");
        if (size is not null)
        {
            width = ReadNumber(size, "CX");
            height = ReadNumber(size, "CY");
        }

        var sizable = true;
        var sizableText = screen.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Style_Sizable")
            ?.Value;
        if (bool.TryParse(sizableText, out var parsed))
        {
            sizable = parsed;
        }

        return new WindowChrome(width, height, sizable);
    }

    private static double? ReadNumber(XElement parent, string name)
    {
        var text = parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
