namespace EqlLoadouts.Core.Content;

/// <summary>
/// Spell gem icons live in gemicons01.tga, gemicons02.tga, and so on.
/// Each sheet is a 10 by 10 grid of 24-pixel icons. Icon 0 is the first cell.
/// </summary>
public static class SpellIconLayout
{
    public const int PerSheet = 100;

    public const int Columns = 10;

    public const int Size = 24;

    public static bool TryLocate(int icon, out int sheetNumber, out int column, out int row)
    {
        if (icon < 0)
        {
            sheetNumber = 0;
            column = 0;
            row = 0;
            return false;
        }

        sheetNumber = icon / PerSheet + 1;
        var cell = icon % PerSheet;
        column = cell % Columns;
        row = cell / Columns;
        return true;
    }
}
