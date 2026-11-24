using System.Globalization;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Layout;

public static class ClientResolution
{
    public static bool TryRead(string? gameDirectory, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return false;
        }

        var path = Path.Combine(gameDirectory, "eqclient.ini");
        if (!File.Exists(path))
        {
            return false;
        }

        IniDocument document;
        try
        {
            document = IniFile.Load(path).Document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var video = document.Find("VideoMode");
        if (video is null)
        {
            return false;
        }

        var fullscreen = !string.Equals(video.Get("Fullscreen"), "0", StringComparison.Ordinal);
        if (fullscreen && TryPair(video, "Width", "Height", out width, out height))
        {
            return true;
        }

        if (!fullscreen && TryPair(video, "WindowedWidth", "WindowedHeight", out width, out height))
        {
            return true;
        }

        return TryPair(video, "Width", "Height", out width, out height)
            || TryPair(video, "WindowedWidth", "WindowedHeight", out width, out height);
    }

    private static bool TryPair(IniSection section, string widthKey, string heightKey, out int width, out int height)
    {
        width = 0;
        height = 0;
        return int.TryParse(section.Get(widthKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
            && int.TryParse(section.Get(heightKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
            && width >= 100
            && height >= 100;
    }
}
