using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Swarmcore.Serialization.BEncoding;

namespace BEncoding.UnitTests;

public sealed class BencodePipeWriterTests
{
    // ─── Dictionary markers ──────────────────────────────────────────────────────

    [Fact]
    public void WriteDictionaryStart_WritesDCharacter()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteDictionaryStart(writer));

        Assert.Single(bytes);
        Assert.Equal((byte)'d', bytes[0]);
    }

    [Fact]
    public void WriteDictionaryEnd_WritesECharacter()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteDictionaryEnd(writer));

        Assert.Single(bytes);
        Assert.Equal((byte)'e', bytes[0]);
    }

    // ─── List markers ────────────────────────────────────────────────────────────

    [Fact]
    public void WriteListStart_WritesLCharacter()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteListStart(writer));

        Assert.Single(bytes);
        Assert.Equal((byte)'l', bytes[0]);
    }

    [Fact]
    public void WriteListEnd_WritesECharacter()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteListEnd(writer));

        Assert.Single(bytes);
        Assert.Equal((byte)'e', bytes[0]);
    }

    // ─── WriteAsciiString (string overload) ──────────────────────────────────────

    [Fact]
    public void WriteAsciiString_String_EncodesLengthPrefixed()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteAsciiString(writer, "hello"));

        Assert.Equal("5:hello", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void WriteAsciiString_EmptyString_WritesZeroLength()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteAsciiString(writer, ""));

        Assert.Equal("0:", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void WriteAsciiString_SingleChar_WritesCorrectly()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteAsciiString(writer, "x"));

        Assert.Equal("1:x", Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData("peers", "5:peers")]
    [InlineData("interval", "8:interval")]
    [InlineData("complete", "8:complete")]
    [InlineData("incomplete", "10:incomplete")]
    [InlineData("failure reason", "14:failure reason")]
    public void WriteAsciiString_TrackerKeywords_EncodesCorrectly(string input, string expected)
    {
        var bytes = Write(writer => BencodePipeWriter.WriteAsciiString(writer, input));

        Assert.Equal(expected, Encoding.ASCII.GetString(bytes));
    }

    // ─── WriteAsciiString (ReadOnlySpan<byte> overload) ──────────────────────────

    [Fact]
    public void WriteAsciiString_ByteSpan_EncodesLengthPrefixed()
    {
        var data = Encoding.ASCII.GetBytes("test");
        var bytes = Write(writer => BencodePipeWriter.WriteAsciiString(writer, data));

        Assert.Equal("4:test", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void WriteAsciiString_EmptyByteSpan_WritesZeroLength()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteAsciiString(writer, ReadOnlySpan<byte>.Empty));

        Assert.Equal("0:", Encoding.ASCII.GetString(bytes));
    }

    // ─── WriteInteger ────────────────────────────────────────────────────────────

    [Fact]
    public void WriteInteger_PositiveValue_EncodesCorrectly()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteInteger(writer, 42));

        Assert.Equal("i42e", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void WriteInteger_Zero_EncodesCorrectly()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteInteger(writer, 0));

        Assert.Equal("i0e", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void WriteInteger_NegativeValue_EncodesCorrectly()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteInteger(writer, -1));

        Assert.Equal("i-1e", Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData(1800, "i1800e")]
    [InlineData(900, "i900e")]
    [InlineData(50, "i50e")]
    [InlineData(100, "i100e")]
    public void WriteInteger_TrackerIntervals_EncodesCorrectly(int value, string expected)
    {
        var bytes = Write(writer => BencodePipeWriter.WriteInteger(writer, value));

        Assert.Equal(expected, Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void WriteInteger_MaxValue_DoesNotThrow()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteInteger(writer, int.MaxValue));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("i", text);
        Assert.EndsWith("e", text);
        Assert.Contains("2147483647", text);
    }

    // ─── WriteByte ───────────────────────────────────────────────────────────────

    [Fact]
    public void WriteByte_WritesSingleByte()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteByte(writer, 0xFF));

        Assert.Single(bytes);
        Assert.Equal(0xFF, bytes[0]);
    }

    // ─── Composite bencode structures ────────────────────────────────────────────

    [Fact]
    public void EmptyDictionary_WritesDE()
    {
        var bytes = Write(static writer =>
        {
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteDictionaryEnd(writer);
        });

        Assert.Equal("de", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void EmptyList_WritesLE()
    {
        var bytes = Write(static writer =>
        {
            BencodePipeWriter.WriteListStart(writer);
            BencodePipeWriter.WriteListEnd(writer);
        });

        Assert.Equal("le", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void TrackerFailureResponse_EncodesCorrectly()
    {
        // d14:failure reason14:invalid requeste
        var bytes = Write(static writer =>
        {
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, "failure reason");
            BencodePipeWriter.WriteAsciiString(writer, "invalid request");
            BencodePipeWriter.WriteDictionaryEnd(writer);
        });

        var result = Encoding.ASCII.GetString(bytes);
        Assert.Equal("d14:failure reason15:invalid requeste", result);
    }

    [Fact]
    public void TrackerAnnounceResponse_EncodesIntervalAndPeers()
    {
        // d8:intervali1800e5:peers0:e
        var bytes = Write(static writer =>
        {
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, "interval");
            BencodePipeWriter.WriteInteger(writer, 1800);
            BencodePipeWriter.WriteAsciiString(writer, "peers");
            BencodePipeWriter.WriteAsciiString(writer, "");
            BencodePipeWriter.WriteDictionaryEnd(writer);
        });

        var result = Encoding.ASCII.GetString(bytes);
        Assert.Equal("d8:intervali1800e5:peers0:e", result);
    }

    [Fact]
    public void NestedDictionary_EncodesCorrectly()
    {
        // d5:innerd3:key5:valueee
        var bytes = Write(static writer =>
        {
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, "inner");
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, "key");
            BencodePipeWriter.WriteAsciiString(writer, "value");
            BencodePipeWriter.WriteDictionaryEnd(writer);
            BencodePipeWriter.WriteDictionaryEnd(writer);
        });

        var result = Encoding.ASCII.GetString(bytes);
        Assert.Equal("d5:innerd3:key5:valueee", result);
    }

    [Fact]
    public void ListOfIntegers_EncodesCorrectly()
    {
        // li1ei2ei3ee
        var bytes = Write(static writer =>
        {
            BencodePipeWriter.WriteListStart(writer);
            BencodePipeWriter.WriteInteger(writer, 1);
            BencodePipeWriter.WriteInteger(writer, 2);
            BencodePipeWriter.WriteInteger(writer, 3);
            BencodePipeWriter.WriteListEnd(writer);
        });

        var result = Encoding.ASCII.GetString(bytes);
        Assert.Equal("li1ei2ei3ee", result);
    }

    [Fact]
    public void ScrapeResponse_EncodesCorrectly()
    {
        // d5:filesd20:xxxxxxxxxxxxxxxxxxxx
        //   d8:completei10e10:downloadedi50e10:incompletei5eeee
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)'x');

        var bytes = Write(writer =>
        {
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, "files");
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, (ReadOnlySpan<byte>)infoHash);
            BencodePipeWriter.WriteDictionaryStart(writer);
            BencodePipeWriter.WriteAsciiString(writer, "complete");
            BencodePipeWriter.WriteInteger(writer, 10);
            BencodePipeWriter.WriteAsciiString(writer, "downloaded");
            BencodePipeWriter.WriteInteger(writer, 50);
            BencodePipeWriter.WriteAsciiString(writer, "incomplete");
            BencodePipeWriter.WriteInteger(writer, 5);
            BencodePipeWriter.WriteDictionaryEnd(writer);
            BencodePipeWriter.WriteDictionaryEnd(writer);
            BencodePipeWriter.WriteDictionaryEnd(writer);
        });

        var result = Encoding.ASCII.GetString(bytes);
        Assert.Contains("5:files", result);
        Assert.Contains("8:completei10e", result);
        Assert.Contains("10:downloadedi50e", result);
        Assert.Contains("10:incompletei5e", result);
    }

    // ─── WriteLength ─────────────────────────────────────────────────────────────

    [Fact]
    public void WriteLength_WritesIntegerWithoutDelimiters()
    {
        var bytes = Write(static writer => BencodePipeWriter.WriteLength(writer, 42));

        Assert.Equal("42", Encoding.ASCII.GetString(bytes));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static byte[] Write(Action<PipeWriter> action)
    {
        var pipe = new Pipe();
        action(pipe.Writer);
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out var result);
        var bytes = result.Buffer.ToArray();
        pipe.Reader.Complete();

        return bytes;
    }
}
