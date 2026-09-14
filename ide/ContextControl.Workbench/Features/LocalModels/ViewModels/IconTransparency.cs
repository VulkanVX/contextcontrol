// CC-DESC: Detects whether bundled PNG icons carry transparent backgrounds.

using Avalonia.Platform;

namespace ContextControl.Workbench.ViewModels;

internal static class IconTransparency
{
    private static readonly Dictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static bool HasTransparentBackground(string? iconSource)
    {
        if (string.IsNullOrWhiteSpace(iconSource))
        {
            return false;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(iconSource, out var cached))
            {
                return cached;
            }

            var hasTransparentBackground = false;
            try
            {
                using var stream = AssetLoader.Open(new Uri(iconSource, UriKind.Absolute));
                hasTransparentBackground = PngDeclaresTransparency(stream);
            }
            catch
            {
                hasTransparentBackground = false;
            }

            Cache[iconSource] = hasTransparentBackground;
            return hasTransparentBackground;
        }
    }

    private static bool PngDeclaresTransparency(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8];
        if (!ReadExactly(stream, signature)
            || signature[0] != 0x89
            || signature[1] != (byte)'P'
            || signature[2] != (byte)'N'
            || signature[3] != (byte)'G'
            || signature[4] != 0x0D
            || signature[5] != 0x0A
            || signature[6] != 0x1A
            || signature[7] != 0x0A)
        {
            return false;
        }

        var hasAlphaChannel = false;
        var chunkHeader = new byte[8];
        var ihdr = new byte[13];
        while (true)
        {
            if (!ReadExactly(stream, chunkHeader))
            {
                return hasAlphaChannel;
            }

            var length = ReadBigEndianInt32(chunkHeader[..4]);
            if (length < 0)
            {
                return hasAlphaChannel;
            }

            var chunkType = new string([
                (char)chunkHeader[4],
                (char)chunkHeader[5],
                (char)chunkHeader[6],
                (char)chunkHeader[7]
            ]);

            if (chunkType == "IHDR")
            {
                if (length < ihdr.Length || !ReadExactly(stream, ihdr))
                {
                    return false;
                }

                var colorType = ihdr[9];
                hasAlphaChannel = colorType is 4 or 6;
                Skip(stream, length - ihdr.Length + 4);
                if (hasAlphaChannel)
                {
                    return true;
                }

                continue;
            }

            if (chunkType == "tRNS")
            {
                return true;
            }

            if (chunkType == "IDAT" || chunkType == "IEND")
            {
                return hasAlphaChannel;
            }

            Skip(stream, length + 4);
        }
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> value)
    {
        return (value[0] << 24)
            | (value[1] << 16)
            | (value[2] << 8)
            | value[3];
    }

    private static void Skip(Stream stream, int byteCount)
    {
        if (byteCount <= 0)
        {
            return;
        }

        if (stream.CanSeek)
        {
            stream.Seek(byteCount, SeekOrigin.Current);
            return;
        }

        Span<byte> buffer = stackalloc byte[256];
        var remaining = byteCount;
        while (remaining > 0)
        {
            var read = stream.Read(buffer[..Math.Min(buffer.Length, remaining)]);
            if (read == 0)
            {
                return;
            }

            remaining -= read;
        }
    }
}
