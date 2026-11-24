using System.Globalization;

namespace EqlLoadouts.Core.Content;

/// <summary>
/// Item icons live in uifiles/default/dragitem1.dds and the sheets after it.
/// Each sheet is a 6 by 6 grid of 40-pixel icons, numbered down the column and then across.
/// dragitem1.dds starts at icon 500. Icons below that are not in these sheets.
/// </summary>
public static class ItemIconLayout
{
    public const int FirstIcon = 500;

    public const int PerSheet = 36;

    public const int Columns = 6;

    public const int Size = 40;

    public static int? Parse(string field)
    {
        if (field.Length < 2 || field[0] is not ('A' or 'a'))
        {
            return null;
        }

        return int.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var icon) && icon >= 0
            ? icon
            : null;
    }

    public static bool TryLocate(int icon, out int sheetNumber, out int column, out int row)
    {
        if (icon < FirstIcon)
        {
            sheetNumber = 0;
            column = 0;
            row = 0;
            return false;
        }

        var index = icon - FirstIcon;
        sheetNumber = index / PerSheet + 1;
        var cell = index % PerSheet;
        column = cell / Columns;
        row = cell % Columns;
        return true;
    }
}
