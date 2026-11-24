namespace EqlLoadouts.Core.Content;

/// <summary>Reads an uncompressed or run-length encoded 32-bit TGA into top-down BGRA pixels.</summary>
public static class TgaReader
{
    public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height, out byte[] pixels)
    {
        width = 0;
        height = 0;
        pixels = [];
        if (data.Length < 18 || data[16] != 32 || (data[17] & 0xC0) != 0)
        {
            return false;
        }

        var imageType = data[2];
        if (imageType is not (2 or 10))
        {
            return false;
        }

        width = data[12] | (data[13] << 8);
        height = data[14] | (data[15] << 8);
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue / 4)
        {
            return false;
        }

        var byteCount = (int)pixelCount * 4;
        var pixelStart = 18 + data[0];
        if (data[1] != 0)
        {
            var entryBytes = (data[7] + 7) / 8;
            var mapBytes = (long)(data[5] | (data[6] << 8)) * entryBytes;
            if (mapBytes > int.MaxValue - pixelStart)
            {
                return false;
            }

            pixelStart += (int)mapBytes;
        }

        if (pixelStart > data.Length)
        {
            return false;
        }

        var stored = new byte[byteCount];
        if (imageType == 2)
        {
            if (data.Length < pixelStart + byteCount)
            {
                return false;
            }

            data.Slice(pixelStart, byteCount).CopyTo(stored);
        }
        else if (!TryDecodeRle(data, pixelStart, stored))
        {
            return false;
        }

        pixels = new byte[byteCount];
        var stride = width * 4;
        var topOrigin = (data[17] & 0x20) != 0;
        for (var y = 0; y < height; y++)
        {
            var sourceY = topOrigin ? y : height - 1 - y;
            Buffer.BlockCopy(stored, sourceY * stride, pixels, y * stride, stride);
        }

        return true;
    }

    private static bool TryDecodeRle(ReadOnlySpan<byte> data, int start, byte[] destination)
    {
        var written = 0;
        var index = start;
        var needed = destination.Length;
        while (written < needed)
        {
            if (index >= data.Length)
            {
                return false;
            }

            var header = data[index++];
            var count = (header & 0x7F) + 1;
            var packetBytes = count * 4;
            if (packetBytes > needed - written)
            {
                return false;
            }

            if ((header & 0x80) != 0)
            {
                if (data.Length - index < 4)
                {
                    return false;
                }

                for (var n = 0; n < count; n++)
                {
                    data.Slice(index, 4).CopyTo(destination.AsSpan(written, 4));
                    written += 4;
                }

                index += 4;
            }
            else
            {
                if (data.Length - index < packetBytes)
                {
                    return false;
                }

                data.Slice(index, packetBytes).CopyTo(destination.AsSpan(written, packetBytes));
                written += packetBytes;
                index += packetBytes;
            }
        }

        return true;
    }
}
