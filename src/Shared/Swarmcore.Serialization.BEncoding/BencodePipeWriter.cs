using System.Buffers.Text;
using System.IO.Pipelines;

namespace Swarmcore.Serialization.BEncoding;

public static class BencodePipeWriter
{
    public static void WriteDictionaryStart(PipeWriter writer) => WriteByte(writer, (byte)'d');

    public static void WriteDictionaryEnd(PipeWriter writer) => WriteByte(writer, (byte)'e');

    public static void WriteListStart(PipeWriter writer) => WriteByte(writer, (byte)'l');

    public static void WriteListEnd(PipeWriter writer) => WriteByte(writer, (byte)'e');

    public static void WriteAsciiString(PipeWriter writer, ReadOnlySpan<byte> value)
    {
        WriteLength(writer, value.Length);
        WriteByte(writer, (byte)':');
        Copy(writer, value);
    }

    public static void WriteAsciiString(PipeWriter writer, string value)
    {
        WriteLength(writer, value.Length);
        WriteByte(writer, (byte)':');
        var span = writer.GetSpan(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            span[index] = (byte)value[index];
        }

        writer.Advance(value.Length);
    }

    public static void WriteInteger(PipeWriter writer, int value)
    {
        WriteByte(writer, (byte)'i');
        WriteNumber(writer, value);
        WriteByte(writer, (byte)'e');
    }

    public static void WriteLength(PipeWriter writer, int value) => WriteNumber(writer, value);

    public static void WriteByte(PipeWriter writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    private static void WriteNumber(PipeWriter writer, int value)
    {
        Span<byte> buffer = stackalloc byte[11];
        Utf8Formatter.TryFormat(value, buffer, out var written);
        Copy(writer, buffer[..written]);
    }

    private static void Copy(PipeWriter writer, ReadOnlySpan<byte> value)
    {
        var span = writer.GetSpan(value.Length);
        value.CopyTo(span);
        writer.Advance(value.Length);
    }
}
