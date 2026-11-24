namespace EqlLoadouts.Core.Content;

/// <summary>Social name colors from the game's social editor, SEW_TC0 through SEW_TC19.</summary>
public static class SocialColors
{
    private static readonly (byte R, byte G, byte B)[] Palette =
    [
        (240, 240, 240),
        (240, 180, 0),
        (0, 128, 0),
        (180, 100, 0),
        (0, 0, 128),
        (240, 0, 240),
        (128, 128, 128),
        (224, 224, 224),
        (130, 130, 0),
        (96, 96, 96),
        (0, 0, 0),
        (140, 0, 0),
        (160, 160, 160),
        (240, 0, 0),
        (0, 240, 0),
        (240, 240, 0),
        (0, 0, 240),
        (0, 100, 240),
        (0, 240, 240),
        (128, 0, 128),
    ];

    public static int Count => Palette.Length;

    public static (byte R, byte G, byte B) At(int index) => Palette[index];

    public static (byte R, byte G, byte B) Rgb(string? color)
    {
        return int.TryParse(color, out var index) && index >= 0 && index < Palette.Length
            ? Palette[index]
            : Palette[0];
    }
}
