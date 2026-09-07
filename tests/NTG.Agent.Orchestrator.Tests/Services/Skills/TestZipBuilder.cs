using System.Buffers.Binary;
using System.Text;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

/// <summary>
/// Writes ZIP archives byte by byte, including ones <see cref="System.IO.Compression.ZipArchive"/>
/// refuses to produce.
/// </summary>
/// <remarks>
/// The importer's security controls are mostly about malformed or hostile archives, and .NET's
/// writer cannot create any of the interesting cases: it will not set the encryption bit, will not
/// write a symlink's Unix mode into external attributes, and normalises away the duplicate and
/// traversal entry names that matter most. Tests built on <c>ZipArchive</c> would therefore assert
/// only against inputs an attacker would never send.
///
/// Entries are stored uncompressed (method 0) by default so the CRC and the two size fields stay
/// trivially correct and each test's intent stays visible. <see cref="AddDeflated"/> opts into real
/// compression, which is what a decompression bomb needs.
/// </remarks>
internal sealed class TestZipBuilder
{
    private readonly List<Entry> _entries = [];

    private sealed record Entry(
        string Name,
        byte[] Content,
        ushort Flags,
        ushort Method,
        uint ExternalAttributes,
        byte HostSystem,
        uint? DeclaredUncompressedSize,
        byte[]? RawStoredBytes = null)
    {
        /// <summary>What actually goes on the wire — the deflate stream for compressed entries.</summary>
        public byte[] Payload => RawStoredBytes ?? Content;
    }

    /// <summary>Adds a normal stored file, marked as a Unix regular file with mode 0644.</summary>
    public TestZipBuilder AddFile(string name, string content) =>
        AddFile(name, Encoding.UTF8.GetBytes(content));

    public TestZipBuilder AddFile(string name, byte[] content)
    {
        _entries.Add(new Entry(name, content, 0, 0, 0x81A4u << 16, 3, null));
        return this;
    }

    /// <summary>Adds an entry with the general-purpose encryption bit set.</summary>
    public TestZipBuilder AddEncrypted(string name, string content)
    {
        _entries.Add(new Entry(name, Encoding.UTF8.GetBytes(content), 0x0001, 0, 0x81A4u << 16, 3, null));
        return this;
    }

    /// <summary>Adds a symlink: S_IFLNK mode, with the link target stored as the content.</summary>
    public TestZipBuilder AddSymlink(string name, string target)
    {
        _entries.Add(new Entry(name, Encoding.UTF8.GetBytes(target), 0, 0, 0xA1FFu << 16, 3, null));
        return this;
    }

    /// <summary>Adds an entry whose Unix mode marks it setuid.</summary>
    public TestZipBuilder AddSetuid(string name, string content)
    {
        _entries.Add(new Entry(name, Encoding.UTF8.GetBytes(content), 0, 0, 0x89EDu << 16, 3, null));
        return this;
    }

    /// <summary>Adds an entry using a compression method the importer must reject by number.</summary>
    public TestZipBuilder AddWithMethod(string name, string content, ushort method)
    {
        _entries.Add(new Entry(name, Encoding.UTF8.GetBytes(content), 0, method, 0x81A4u << 16, 3, null));
        return this;
    }

    /// <summary>
    /// Adds an entry whose header understates its real size — the forgery that makes a
    /// compression-ratio cap useless and that the streaming counter exists to catch.
    /// </summary>
    public TestZipBuilder AddWithForgedSize(string name, byte[] content, uint declaredSize)
    {
        _entries.Add(new Entry(name, content, 0, 0, 0x81A4u << 16, 3, declaredSize));
        return this;
    }

    /// <summary>
    /// Adds a genuinely deflate-compressed entry, optionally declaring a false uncompressed size.
    /// This is the only way to build a real decompression bomb: highly compressible content stays
    /// small on the wire while expanding without bound on read, and understating the declared size
    /// slips past any check that trusts the header.
    /// </summary>
    public TestZipBuilder AddDeflated(string name, byte[] content, uint? declaredSize = null)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(
                   compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(content);
        }

        _entries.Add(new Entry(name, content, 0, 8, 0x81A4u << 16, 3, declaredSize, compressed.ToArray()));
        return this;
    }

    public byte[] Build()
    {
        using var output = new MemoryStream();
        var offsets = new List<uint>(_entries.Count);

        foreach (var entry in _entries)
        {
            offsets.Add((uint)output.Position);

            var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            var crc = Crc32(entry.Content);
            var size = entry.DeclaredUncompressedSize ?? (uint)entry.Content.Length;

            var header = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0x04034B50);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), entry.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), entry.Method);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)entry.Payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), size);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)nameBytes.Length);

            output.Write(header);
            output.Write(nameBytes);
            output.Write(entry.Payload);
        }

        var centralDirectoryOffset = (uint)output.Position;

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            var crc = Crc32(entry.Content);
            var size = entry.DeclaredUncompressedSize ?? (uint)entry.Content.Length;

            var header = new byte[46];
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0x02014B50);
            header[4] = 20;
            header[5] = entry.HostSystem;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), entry.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), entry.Method);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)entry.Payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), size);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), (ushort)nameBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(38), entry.ExternalAttributes);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(42), offsets[i]);

            output.Write(header);
            output.Write(nameBytes);
        }

        var centralDirectorySize = (uint)output.Position - centralDirectoryOffset;

        var eocd = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), (ushort)_entries.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), (ushort)_entries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), centralDirectorySize);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), centralDirectoryOffset);
        output.Write(eocd);

        return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
