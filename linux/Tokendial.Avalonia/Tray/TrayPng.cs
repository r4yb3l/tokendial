using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Tokendial.Linux.Tray;

/// <summary>
/// Writes the tray icon's pixels as a PNG without SkiaSharp. libSkiaSharp 3.119.4 segfaults intermittently
/// inside <c>SkPngEncoder::Encode</c> (a 64-byte load past the pixel buffer in its AVX-512 colour transform),
/// and on X11 every <c>WindowIcon</c> built from a bitmap goes through that encoder; a stream does not.
/// </summary>
internal static class TrayPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes premultiplied BGRA, the layout a render target copies out, as 8-bit RGBA.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> premultipliedBgra)
    {
        if (premultipliedBgra.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height}.", nameof(premultipliedBgra));

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;

        var scanlines = new byte[height * (1 + width * 4)];
        var o = 0;
        for (var y = 0; y < height; y++)
        {
            scanlines[o++] = 0;
            for (var x = 0; x < width; x++)
            {
                var p = premultipliedBgra.Slice((y * width + x) * 4, 4);
                var a = p[3];
                scanlines[o++] = Unpremultiply(p[2], a);
                scanlines[o++] = Unpremultiply(p[1], a);
                scanlines[o++] = Unpremultiply(p[0], a);
                scanlines[o++] = a;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(scanlines);

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte Unpremultiply(byte channel, byte alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);

    private static void WriteChunk(Stream png, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc(typeBytes, data));
        png.Write(word);
    }

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
