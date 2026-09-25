using System.Buffers.Binary;
using System.Text;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// A minimal, read-only parse of a ZIP central directory, used by
/// <see cref="SkillPackageImporter"/> before <see cref="System.IO.Compression.ZipArchive"/> is
/// ever constructed.
/// </summary>
/// <remarks>
/// <para>
/// This exists because three controls the import security matrix requires are not reachable
/// through <c>ZipArchive</c>'s public API: the general-purpose <b>encryption bit</b>, the
/// <b>compression method</b>, and the Unix mode bits in <b>external attributes</b> that mark an
/// entry as a symlink. <c>ZipArchiveEntry</c> exposes none of the first two, and reading an
/// encrypted entry surfaces only as a generic <c>InvalidDataException</c> at decompression time —
/// far too late, and indistinguishable from corruption.
/// </para>
/// <para>
/// It also yields an exact entry count from the header rather than from <c>archive.Entries</c>,
/// whose enumeration allocates an object per entry: 200k entries cost ~22 MB on the wire but tens
/// of megabytes of heap merely to count them, so a check that reads <c>Entries.Count</c> runs only
/// after the cost it was meant to prevent has already been paid.
/// </para>
/// <para>
/// Deliberately not a general-purpose ZIP reader. ZIP64 and multi-disk archives are rejected
/// rather than parsed — a skill package is a handful of small text files, so the formats exist
/// here only as evasion vectors.
/// </para>
/// </remarks>
internal static class ZipCentralDirectory
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;   // PK\x05\x06
    private const uint CentralFileHeaderSignature = 0x02014B50;       // PK\x01\x02
    private const int EndOfCentralDirectoryLength = 22;
    private const int CentralFileHeaderLength = 46;

    /// <summary>Max ZIP comment (0xFFFF) plus the fixed EOCD record itself.</summary>
    private const int MaxEndOfCentralDirectorySearch = 0xFFFF + EndOfCentralDirectoryLength;

    /// <summary>Host system 3 in "version made by" means Unix, so the high attribute word is st_mode.</summary>
    private const byte UnixHostSystem = 3;

    internal sealed record Entry(
        string FileName,
        ushort GeneralPurposeFlags,
        ushort CompressionMethod,
        uint UncompressedSize,
        uint ExternalAttributes,
        byte HostSystem)
    {
        /// <summary>General-purpose bit 0. Set on encrypted entries, which scanners cannot read.</summary>
        public bool IsEncrypted => (GeneralPurposeFlags & 0x0001) != 0;

        /// <summary>The Unix <c>st_mode</c> word, or 0 when the archive was not made on Unix.</summary>
        public uint UnixMode => HostSystem == UnixHostSystem ? ExternalAttributes >> 16 : 0;

        /// <summary>
        /// S_IFLNK. A symlink entry stores its target as file content, so extracting one is a
        /// write outside the package that no path check on <see cref="FileName"/> can see.
        /// </summary>
        public bool IsSymlink => (UnixMode & 0xF000) == 0xA000;

        /// <summary>Anything that is not S_IFREG or S_IFDIR: device nodes, sockets, FIFOs.</summary>
        public bool IsSpecialFile
        {
            get
            {
                var kind = UnixMode & 0xF000;
                return kind != 0 && kind != 0x8000 && kind != 0x4000;
            }
        }

        /// <summary>setuid or setgid.</summary>
        public bool HasElevatedBits => (UnixMode & 0x0800) != 0 || (UnixMode & 0x0400) != 0;

        /// <summary>Trailing '/' is the conventional directory marker, independent of Unix mode.</summary>
        public bool IsDirectoryMarker =>
            FileName.EndsWith('/') || (UnixMode & 0xF000) == 0x4000;
    }

    internal sealed record Result(IReadOnlyList<Entry> Entries, string? Error)
    {
        public bool Ok => Error is null;
    }

    /// <summary>
    /// Parses the central directory of <paramref name="bytes"/>, stopping at the first structural
    /// problem. <paramref name="maxEntries"/> is enforced from the EOCD count, before any
    /// per-entry work.
    /// </summary>
    public static Result Read(ReadOnlySpan<byte> bytes, int maxEntries)
    {
        if (bytes.Length < EndOfCentralDirectoryLength)
        {
            return new Result([], "file is too small to be a ZIP archive");
        }

        var eocd = FindEndOfCentralDirectory(bytes);
        if (eocd < 0)
        {
            return new Result([], "no ZIP end-of-central-directory record found");
        }

        var record = bytes[eocd..];

        var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (diskNumber != 0 || centralDirectoryDisk != 0)
        {
            return new Result([], "multi-disk ZIP archives are not supported");
        }

        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        var centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

        // 0xFFFF/0xFFFFFFFF are the ZIP64 escape values. Rejected rather than followed: a skill
        // package has no legitimate reason to need ZIP64.
        if (entryCount == 0xFFFF || centralDirectorySize == 0xFFFFFFFF || centralDirectoryOffset == 0xFFFFFFFF)
        {
            return new Result([], "ZIP64 archives are not supported");
        }

        if (entryCount == 0)
        {
            return new Result([], "archive contains no entries");
        }

        if (entryCount > maxEntries)
        {
            return new Result([], $"archive declares {entryCount} entries, limit is {maxEntries}");
        }

        if (centralDirectoryOffset > (uint)bytes.Length
            || centralDirectorySize > (uint)bytes.Length - centralDirectoryOffset)
        {
            return new Result([], "central directory extends past the end of the file");
        }

        var entries = new List<Entry>(entryCount);
        var cursor = (int)centralDirectoryOffset;
        var end = cursor + (int)centralDirectorySize;

        for (var i = 0; i < entryCount; i++)
        {
            if (cursor + CentralFileHeaderLength > end)
            {
                return new Result([], "central directory is truncated");
            }

            var header = bytes[cursor..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralFileHeaderSignature)
            {
                return new Result([], $"central directory entry {i} has a bad signature");
            }

            var hostSystem = header[5];  // high byte of "version made by"
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
            var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            var externalAttributes = BinaryPrimitives.ReadUInt32LittleEndian(header[38..]);

            var total = CentralFileHeaderLength + nameLength + extraLength + commentLength;
            if (cursor + total > end)
            {
                return new Result([], $"central directory entry {i} extends past the directory");
            }

            // Bit 11 selects UTF-8; otherwise the spec says CP437. Latin1 is used as the stand-in
            // so every byte round-trips to a character and nothing is silently replaced — the
            // name is only ever compared against an ASCII allowlist downstream.
            var nameBytes = bytes.Slice(cursor + CentralFileHeaderLength, nameLength);
            var name = (flags & 0x0800) != 0
                ? Encoding.UTF8.GetString(nameBytes)
                : Encoding.Latin1.GetString(nameBytes);

            entries.Add(new Entry(name, flags, method, uncompressedSize, externalAttributes, hostSystem));
            cursor += total;
        }

        return new Result(entries, null);
    }

    /// <summary>Scans backwards for the EOCD signature, tolerating a trailing archive comment.</summary>
    private static int FindEndOfCentralDirectory(ReadOnlySpan<byte> bytes)
    {
        var limit = Math.Min(bytes.Length, MaxEndOfCentralDirectorySearch);

        for (var offset = bytes.Length - EndOfCentralDirectoryLength; offset >= bytes.Length - limit; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]) == EndOfCentralDirectorySignature)
            {
                return offset;
            }
        }

        return -1;
    }
}
