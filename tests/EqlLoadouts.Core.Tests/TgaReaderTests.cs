using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.Core.Tests;

public class TgaReaderTests
{
    [Fact]
    public void Uncompressed_FlipsBottomOriginRows()
    {
        var stored = new byte[]
        {
            1, 0, 0, 255, 2, 0, 0, 255,
            3, 0, 0, 255, 4, 0, 0, 255,
        };

        Assert.True(TgaReader.TryRead(Tga(2, 2, 0x08, 2, stored), out var width, out var height, out var pixels));

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(new byte[] { 3, 4, 1, 2 }, Blue(pixels));
    }

    [Fact]
    public void Uncompressed_KeepsTopOriginOrder()
    {
        var stored = new byte[]
        {
            3, 0, 0, 255, 4, 0, 0, 255,
            1, 0, 0, 255, 2, 0, 0, 255,
        };

        Assert.True(TgaReader.TryRead(Tga(2, 2, 0x28, 2, stored), out _, out _, out var pixels));

        Assert.Equal(new byte[] { 3, 4, 1, 2 }, Blue(pixels));
    }

    [Fact]
    public void RunLength_DecodesPacketsAndFlipsRows()
    {
        var stored = new byte[]
        {
            0x81, 9, 0, 0, 255,
            0x01, 1, 0, 0, 255, 2, 0, 0, 255,
        };

        Assert.True(TgaReader.TryRead(Tga(2, 2, 0x08, 10, stored), out _, out _, out var pixels));

        Assert.Equal(new byte[] { 1, 2, 9, 9 }, Blue(pixels));
    }

    [Fact]
    public void RunLength_RejectsAShortPacket()
    {
        Assert.False(TgaReader.TryRead(Tga(1, 1, 0x08, 10, [0x80]), out _, out _, out _));
    }

    [Fact]
    public void Uncompressed_RejectsAShortImage()
    {
        Assert.False(TgaReader.TryRead(Tga(2, 2, 0x08, 2, new byte[4]), out _, out _, out _));
    }

    [Fact]
    public void Alternate3GemSheet_DecodesTheRunLengthImage()
    {
        var path = Path.Combine(
            LoadoutDiscovery.DefaultGameDirectory,
            "Textures",
            "Alternate 3",
            "gemicons02.tga");
        if (!File.Exists(path))
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(10, bytes[2]);
        Assert.True(TgaReader.TryRead(bytes, out var width, out var height, out var pixels));
        Assert.Equal(256, width);
        Assert.Equal(256, height);
        Assert.Contains(pixels, pixel => pixel != 0);
    }

    private static byte[] Tga(int width, int height, byte descriptor, byte imageType, byte[] stored)
    {
        var bytes = new byte[18 + stored.Length];
        bytes[2] = imageType;
        bytes[12] = (byte)width;
        bytes[13] = (byte)(width >> 8);
        bytes[14] = (byte)height;
        bytes[15] = (byte)(height >> 8);
        bytes[16] = 32;
        bytes[17] = descriptor;
        stored.CopyTo(bytes, 18);
        return bytes;
    }

    private static byte[] Blue(byte[] pixels)
    {
        var blue = new byte[pixels.Length / 4];
        for (var i = 0; i < blue.Length; i++)
        {
            blue[i] = pixels[i * 4];
        }

        return blue;
    }
}
