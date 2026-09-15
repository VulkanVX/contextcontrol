using System.Text;

namespace ContextControl.Workbench.Services;

/// <summary>Reads bounded GGUF metadata only; never loads weight tensors into memory.</summary>
internal static class GgufResourceMetadata
{
    internal static LocalModelMemory Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3))
            throw new InvalidDataException("Auto adaptation needs a GGUF v2/v3 file.");
        _ = reader.ReadUInt64();
        var count = reader.ReadUInt64();
        if (count > 100000) throw new InvalidDataException("GGUF metadata is too large.");
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        for (ulong i = 0; i < count; i++)
        {
            if (stream.Position > 64 * 1024 * 1024) throw new InvalidDataException("GGUF metadata exceeds the reading budget.");
            var key = Text(4096);
            var type = reader.ReadUInt32();
            var wanted = key == "general.architecture" || key.EndsWith(".block_count") || key.EndsWith(".context_length")
                || key.EndsWith(".embedding_length") || key.Contains(".attention.") || key == "split.count";
            var value = Value(type, wanted, 0);
            if (wanted && value is not null) values[key] = value;
        }
        var architecture = values.GetValueOrDefault("general.architecture") as string ?? "";
        double? Number(string key) => values.TryGetValue(key, out var value) && value is not string
            ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) : null;
        int? Integer(string key) => Number(key) is > 0 and <= int.MaxValue and var n ? (int)n : null;
        if (Number("split.count") is > 1)
            throw new InvalidDataException("Auto adaptation cannot estimate split GGUF weights yet. Use manual settings for this model.");
        var layers = Integer(architecture + ".block_count");
        var context = Integer(architecture + ".context_length");
        var embedding = Number(architecture + ".embedding_length");
        var heads = Number(architecture + ".attention.head_count");
        var kvHeads = Number(architecture + ".attention.head_count_kv") ?? heads;
        var keyLength = Number(architecture + ".attention.key_length") ?? (heads is > 0 ? embedding / heads : null);
        var valueLength = Number(architecture + ".attention.value_length") ?? keyLength;
        var kv = layers * kvHeads * (keyLength + valueLength) * 2; // default f16 K + V
        return new(stream.Length / 1073741824d, layers, kv, context);

        string Text(int limit)
        {
            var length = reader.ReadUInt64();
            if (length > (ulong)limit || length > (ulong)(stream.Length - stream.Position))
                throw new InvalidDataException("Invalid GGUF string length.");
            return Encoding.UTF8.GetString(reader.ReadBytes((int)length));
        }
        void Skip(ulong bytes)
        {
            if (bytes > (ulong)(stream.Length - stream.Position)) throw new EndOfStreamException();
            stream.Seek((long)bytes, SeekOrigin.Current);
        }
        object? Value(uint type, bool capture, int depth)
        {
            if (depth > 1) throw new InvalidDataException("Nested GGUF arrays are unsupported.");
            if (type == 8)
            {
                if (capture) return Text(4096);
                Skip(reader.ReadUInt64());
                return null;
            }
            if (type == 9)
            {
                var element = reader.ReadUInt32();
                var size = reader.ReadUInt64();
                if (size > 2000000) throw new InvalidDataException("GGUF array exceeds the reading budget.");
                var width = Width(element);
                if (width > 0) Skip(checked(size * (ulong)width));
                else for (ulong i = 0; i < size; i++) _ = Value(element, false, depth + 1);
                return null;
            }
            if (!capture)
            {
                var width = Width(type);
                if (width == 0) throw new InvalidDataException("Unknown GGUF metadata type.");
                Skip((ulong)width);
                return null;
            }
            return type switch
            {
                0 => reader.ReadByte(), 1 => reader.ReadSByte(), 2 => reader.ReadUInt16(), 3 => reader.ReadInt16(),
                4 => reader.ReadUInt32(), 5 => reader.ReadInt32(), 6 => reader.ReadSingle(), 7 => reader.ReadBoolean(),
                10 => reader.ReadUInt64(), 11 => reader.ReadInt64(), 12 => reader.ReadDouble(),
                _ => throw new InvalidDataException("Unknown GGUF metadata type.")
            };
        }
    }
    private static int Width(uint type) => type switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
}
