using System.IO.Compression;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Pak.Objects;

namespace RegistryMerger;

/// <summary>
/// Reads a file out of a pak. Zlib-compressed entries are inflated here with .NET's ZLibStream: CUE4Parse needs the
/// native zlib-ng library for them, which this program doesn't ship. Everything else goes through CUE4Parse.
/// </summary>
public static class PakEntryReader
{
    public static byte[] Read(GameFile file)
    {
        if (file is FPakEntry { IsCompressed: true, CompressionMethod: CompressionMethod.Zlib } entry)
            return ReadZlib(entry);
        if (file is FPakEntry { IsCompressed: true } other)
            throw new NotSupportedException($"{file.Path}: {other.CompressionMethod} compression is not supported.");
        return file.Read();
    }

    private static byte[] ReadZlib(FPakEntry entry)
    {
        if (entry.IsEncrypted) throw new NotSupportedException($"{entry.Path}: encrypted and compressed entries are not supported.");
        var size = checked((int)entry.UncompressedSize);
        var output = new byte[size];
        using var pak = new FileStream(entry.PakFileReader.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var written = 0;
        foreach (var block in entry.CompressionBlocks)
        {
            var compressed = new byte[block.Size];
            pak.Position = block.CompressedStart;
            pak.ReadExactly(compressed);
            var want = Math.Min((int)entry.CompressionBlockSize, size - written);
            using var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            z.ReadExactly(output, written, want);
            written += want;
        }
        if (written != size) throw new InvalidDataException($"{entry.Path}: inflated {written} of {size} bytes.");
        return output;
    }
}
