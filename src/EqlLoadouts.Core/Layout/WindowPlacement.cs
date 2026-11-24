using System.Globalization;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Layout;

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height);

public static class WindowPlacement
{
    public static bool TryParsePercent(string? text, out double percent)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith('%'))
        {
            trimmed = trimmed[..^1];
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
    }

    public static string FormatPercent(double percent)
        => FormatPercent(percent, 0);

    /// <summary>
    /// Percent text with only the decimal places required to keep the window within 1/20 of a pixel
    /// on a screen of <paramref name="referencePixels"/>.
    /// A pixel that is already on an integer is kept on the side of that integer where the client's
    /// cast to int (truncation toward zero) still lands on it. Rounding the percent the other way
    /// drops a pixel, which opens a one-pixel gap on every other stacked window.
    /// </summary>
    public static string FormatPercent(double percent, double referencePixels)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return "0%";
        }

        if (referencePixels > 0)
        {
            var pixels = percent / 100d * referencePixels;
            var nearest = Math.Round(pixels, MidpointRounding.AwayFromZero);
            // Only a pixel that is already on an integer is nudged onto the side the client's
            // cast to int keeps. Moving a half-pixel position would shift the window.
            if (Math.Abs(pixels - nearest) <= 0.05
                && TryFormatClientPixel(pixels, nearest, referencePixels, out var text))
            {
                return text;
            }
        }

        return FormatRounded(percent, DigitsWithin(percent, referencePixels));
    }

    public static string FormatAxisPercent(string? anchor, double percent, double screen, double centerScale)
        => FormatPercent(percent, Reference(anchor, screen, centerScale));

    public static bool SameOffset(string? anchor, double left, double right, double screen, double centerScale)
    {
        var reference = Math.Max(Reference(anchor, screen, centerScale), 1);
        return Math.Abs(left - right) / 100d * reference <= 0.05;
    }

    public static double ToPixelsX(string? xRef, double xPercent, double width, double screenWidth, double centerScale = 1)
    {
        var offset = xPercent / 100d * Reference(xRef, screenWidth, centerScale);
        return (xRef ?? "left").ToLowerInvariant() switch
        {
            "right" => screenWidth - offset - width,
            "center" => screenWidth / 2d + offset - width / 2d,
            _ => offset,
        };
    }

    public static double ToPixelsY(string? yRef, double yPercent, double height, double screenHeight, double centerScale = 1)
    {
        var offset = yPercent / 100d * Reference(yRef, screenHeight, centerScale);
        return (yRef ?? "top").ToLowerInvariant() switch
        {
            "bottom" => screenHeight - offset - height,
            "center" => screenHeight / 2d + offset - height / 2d,
            _ => offset,
        };
    }

    public static double ToPercentX(string? xRef, double left, double width, double screenWidth, double centerScale = 1)
    {
        var reference = Reference(xRef, screenWidth, centerScale);
        if (reference == 0)
        {
            return 0;
        }

        var offset = (xRef ?? "left").ToLowerInvariant() switch
        {
            "right" => screenWidth - (left + width),
            "center" => left + width / 2d - screenWidth / 2d,
            _ => left,
        };
        return offset / reference * 100d;
    }

    public static double ToPercentY(string? yRef, double top, double height, double screenHeight, double centerScale = 1)
    {
        var reference = Reference(yRef, screenHeight, centerScale);
        if (reference == 0)
        {
            return 0;
        }

        var offset = (yRef ?? "top").ToLowerInvariant() switch
        {
            "bottom" => screenHeight - (top + height),
            "center" => top + height / 2d - screenHeight / 2d,
            _ => top,
        };
        return offset / reference * 100d;
    }

    // INIVersion 1 stores a center offset as a percentage of half the screen.
    // Later versions store it as a percentage of the whole screen.
    public static double CenterScale(IniSection section)
        => section.Get("INIVersion") == "1" ? 0.5d : 1d;

    public static bool HasPosition(IniSection section)
    {
        return section.Get("XPos") is not null
            || section.Get("YPos") is not null
            || section.Get("XRef") is not null
            || section.Get("YRef") is not null;
    }

    public static ScreenRect ReadRect(IniSection section, double screenWidth, double screenHeight, double defaultWidth, double defaultHeight)
    {
        TryParsePercent(section.Get("XPos"), out var xPercent);
        TryParsePercent(section.Get("YPos"), out var yPercent);
        var width = ReadSize(section.Get("Width"), defaultWidth);
        var height = ReadSize(section.Get("Height"), defaultHeight);
        var scale = CenterScale(section);
        return new ScreenRect(
            ToPixelsX(section.Get("XRef"), xPercent, width, screenWidth, scale),
            ToPixelsY(section.Get("YRef"), yPercent, height, screenHeight, scale),
            width,
            height);
    }

    public static void WriteRect(IniSection section, ScreenRect rect, double screenWidth, double screenHeight, bool writeSize)
    {
        var xRef = section.Get("XRef") ?? "left";
        var yRef = section.Get("YRef") ?? "top";
        if (section.Get("XRef") is null)
        {
            section.Set("XRef", xRef);
        }

        if (section.Get("YRef") is null)
        {
            section.Set("YRef", yRef);
        }

        var scale = CenterScale(section);
        section.Set("XPos", FormatAxisPercent(xRef, ToPercentX(xRef, rect.Left, rect.Width, screenWidth, scale), screenWidth, scale));
        section.Set("YPos", FormatAxisPercent(yRef, ToPercentY(yRef, rect.Top, rect.Height, screenHeight, scale), screenHeight, scale));
        if (writeSize)
        {
            section.Set("Width", Math.Round(rect.Width).ToString(CultureInfo.InvariantCulture));
            section.Set("Height", Math.Round(rect.Height).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// A new width or height keeps the edge named by the ini anchor. Left, top, and center stay put on that axis.
    /// </summary>
    public static ScreenRect KeepAnchor(ScreenRect current, double width, double height, string? xRef, string? yRef)
    {
        var left = (xRef ?? "left").ToLowerInvariant() switch
        {
            "right" => current.Left + current.Width - width,
            "center" => current.Left + ((current.Width - width) / 2d),
            _ => current.Left,
        };
        var top = (yRef ?? "top").ToLowerInvariant() switch
        {
            "bottom" => current.Top + current.Height - height,
            "center" => current.Top + ((current.Height - height) / 2d),
            _ => current.Top,
        };
        return new ScreenRect(left, top, width, height);
    }

    public static bool HasStoredSize(IniSection section)
        => double.TryParse(section.Get("Width"), NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && double.TryParse(section.Get("Height"), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static int DigitsWithin(double percent, double referencePixels)
    {
        if (!(referencePixels > 0))
        {
            return 6;
        }

        var digits = 0;
        while (digits < 6)
        {
            var candidate = Math.Round(percent, digits, MidpointRounding.AwayFromZero);
            var error = Math.Abs(candidate - percent) / 100d * referencePixels;
            if (error <= 0.05)
            {
                break;
            }

            digits++;
        }

        return digits;
    }

    private static string FormatRounded(double percent, int digits)
    {
        var value = Math.Round(percent, digits, MidpointRounding.AwayFromZero);
        if (value == 0)
        {
            value = 0;
        }

        return value.ToString("0.######", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// Shortest percent whose float conversion truncates and rounds to <paramref name="target"/>,
    /// while staying within 1/20 of a pixel of <paramref name="pixels"/>.
    /// </summary>
    private static bool TryFormatClientPixel(double pixels, double target, double reference, out string text)
    {
        text = string.Empty;
        double minPixel;
        double maxPixel;
        if (target >= 0)
        {
            minPixel = Math.Max(target, pixels - 0.05);
            maxPixel = Math.Min(target + 0.49, pixels + 0.05);
        }
        else
        {
            minPixel = Math.Max(target - 0.49, pixels - 0.05);
            maxPixel = Math.Min(target, pixels + 0.05);
        }

        if (minPixel > maxPixel)
        {
            return false;
        }

        var ideal = target / reference * 100d;
        var away = target >= 0 ? 1 : -1;
        for (var digits = 0; digits <= 6; digits++)
        {
            var step = Math.Pow(10, -digits);
            var rounded = Math.Round(ideal, digits, MidpointRounding.AwayFromZero);
            for (var nudge = 0; nudge <= 2; nudge++)
            {
                var signs = nudge == 0 ? new[] { 0 } : new[] { away, -away };
                foreach (var sign in signs)
                {
                    var candidate = rounded + (sign * nudge * step);
                    var reconstructed = candidate / 100d * reference;
                    if (reconstructed < minPixel - 1e-6 || reconstructed > maxPixel + 1e-6)
                    {
                        continue;
                    }

                    var formatted = FormatRounded(candidate, digits);
                    if (ClientKeepsPixel(formatted, reference, target))
                    {
                        text = formatted;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ClientKeepsPixel(string text, double reference, double target)
    {
        var number = text.EndsWith('%') ? text[..^1] : text;
        if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return false;
        }

        var basis = (float)reference;
        var targetPixel = (int)target;
        return KeepsClientPixel(basis * percent / 100f, targetPixel)
            && KeepsClientPixel(percent * basis / 100f, targetPixel);
    }

    private static bool KeepsClientPixel(float raw, int target)
    {
        if (float.IsNaN(raw) || float.IsInfinity(raw))
        {
            return false;
        }

        var truncated = (int)raw;
        var rounded = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return truncated == target && rounded == target;
    }

    private static double Reference(string? anchor, double screen, double centerScale)
        => string.Equals(anchor, "center", StringComparison.OrdinalIgnoreCase) ? screen * centerScale : screen;

    private static double ReadSize(string? text, double fallback)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : fallback;
    }
}
