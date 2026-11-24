using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EqlLoadouts.Core.Content;

namespace EqlLoadouts.App.Models;

/// <summary>Crops item icons out of the game's dragitem#.dds sheets.</summary>
public sealed class ItemIcons
{
    private readonly string? _folder;
    private readonly Dictionary<int, BitmapSource?> _sheets = [];
    private readonly Dictionary<int, ImageSource?> _icons = [];

    private ItemIcons(string? folder)
    {
        _folder = folder;
    }

    public static ItemIcons Empty { get; } = new(null);

    public static ItemIcons Load(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return Empty;
        }

        var folder = Path.Combine(gameDirectory, "uifiles", "default");
        return File.Exists(Path.Combine(folder, "dragitem1.dds")) ? new ItemIcons(folder) : Empty;
    }

    public ImageSource? For(int icon)
    {
        if (!ItemIconLayout.TryLocate(icon, out var sheetNumber, out var column, out var row))
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

        var size = ItemIconLayout.Size;
        if ((column + 1) * size > sheet.PixelWidth || (row + 1) * size > sheet.PixelHeight)
        {
            _icons[icon] = null;
            return null;
        }

        var crop = new CroppedBitmap(sheet, new System.Windows.Int32Rect(column * size, row * size, size, size));
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

        sheet = LoadSheet(Path.Combine(_folder, $"dragitem{sheetNumber}.dds"));
        _sheets[sheetNumber] = sheet;
        return sheet;
    }

    private static BitmapSource? LoadSheet(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128 || Encoding.ASCII.GetString(bytes, 0, 4) != "DDS ")
        {
            return null;
        }

        var height = BitConverter.ToInt32(bytes, 12);
        var width = BitConverter.ToInt32(bytes, 16);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            return null;
        }

        var fourcc = Encoding.ASCII.GetString(bytes, 84, 4);
        byte[]? pixels = fourcc switch
        {
            "DXT5" => DecodeBlock(bytes, width, height, explicitAlpha: false),
            "DXT3" => DecodeBlock(bytes, width, height, explicitAlpha: true),
            _ => BitConverter.ToInt32(bytes, 88) == 32 ? CopyBgra(bytes, width, height) : null,
        };
        if (pixels is null)
        {
            return null;
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private static byte[]? CopyBgra(byte[] bytes, int width, int height)
    {
        var pitch = BitConverter.ToInt32(bytes, 20);
        if (pitch < width * 4)
        {
            pitch = width * 4;
        }

        if (bytes.Length < 128 + (long)height * pitch)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(bytes, 128 + y * pitch, pixels, y * width * 4, width * 4);
        }

        return pixels;
    }

    private static byte[]? DecodeBlock(byte[] bytes, int width, int height, bool explicitAlpha)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        if (bytes.Length < 128 + (long)blocksX * blocksY * 16)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        var p = 128;
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var alphas = new int[16];
                if (explicitAlpha)
                {
                    ulong packed = 0;
                    for (var n = 0; n < 8; n++)
                    {
                        packed |= (ulong)bytes[p + n] << (8 * n);
                    }

                    for (var n = 0; n < 16; n++)
                    {
                        var a = (int)((packed >> (4 * n)) & 15);
                        alphas[n] = (a << 4) | a;
                    }
                }
                else
                {
                    var a0 = bytes[p];
                    var a1 = bytes[p + 1];
                    ulong packed = 0;
                    for (var n = 0; n < 6; n++)
                    {
                        packed |= (ulong)bytes[p + 2 + n] << (8 * n);
                    }

                    var table = new int[8];
                    table[0] = a0;
                    table[1] = a1;
                    if (a0 > a1)
                    {
                        for (var n = 1; n <= 6; n++)
                        {
                            table[n + 1] = ((6 - n) * a0 + n * a1) / 6;
                        }
                    }
                    else
                    {
                        table[2] = (4 * a0 + a1) / 5;
                        table[3] = (3 * a0 + 2 * a1) / 5;
                        table[4] = (2 * a0 + 3 * a1) / 5;
                        table[5] = (a0 + 4 * a1) / 5;
                        table[6] = 0;
                        table[7] = 255;
                    }

                    for (var n = 0; n < 16; n++)
                    {
                        alphas[n] = table[(int)((packed >> (3 * n)) & 7)];
                    }
                }

                var c0 = (ushort)(bytes[p + 8] | (bytes[p + 9] << 8));
                var c1 = (ushort)(bytes[p + 10] | (bytes[p + 11] << 8));
                var bits = (uint)bytes[p + 12]
                    | ((uint)bytes[p + 13] << 8)
                    | ((uint)bytes[p + 14] << 16)
                    | ((uint)bytes[p + 15] << 24);
                p += 16;
                Unpack(c0, out var r0, out var g0, out var b0);
                Unpack(c1, out var r1, out var g1, out var b1);
                var red = new[] { r0, r1, (byte)((2 * r0 + r1) / 3), (byte)((r0 + 2 * r1) / 3) };
                var green = new[] { g0, g1, (byte)((2 * g0 + g1) / 3), (byte)((g0 + 2 * g1) / 3) };
                var blue = new[] { b0, b1, (byte)((2 * b0 + b1) / 3), (byte)((b0 + 2 * b1) / 3) };
                for (var py = 0; py < 4; py++)
                {
                    var y = by * 4 + py;
                    if (y >= height)
                    {
                        continue;
                    }

                    for (var px = 0; px < 4; px++)
                    {
                        var x = bx * 4 + px;
                        if (x >= width)
                        {
                            continue;
                        }

                        var index = py * 4 + px;
                        var color = (int)((bits >> (2 * index)) & 3);
                        var o = (y * width + x) * 4;
                        pixels[o] = blue[color];
                        pixels[o + 1] = green[color];
                        pixels[o + 2] = red[color];
                        pixels[o + 3] = (byte)alphas[index];
                    }
                }
            }
        }

        return pixels;
    }

    private static void Unpack(ushort color, out byte red, out byte green, out byte blue)
    {
        var r = (color >> 11) & 31;
        var g = (color >> 5) & 63;
        var b = color & 31;
        red = (byte)((r << 3) | (r >> 2));
        green = (byte)((g << 2) | (g >> 4));
        blue = (byte)((b << 3) | (b >> 2));
    }
}
