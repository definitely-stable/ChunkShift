using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// PATCH-TREE-001 T4-A: a disposable, research-only canonical tree identity model.
/// This is NOT a public API, accepted wire format, filesystem writer or trust policy.
/// </summary>
internal static class PatchTreeManifestCandidate
{
    internal const string Schema = "chunkshift.patch-tree-candidate.t4a.v0";
    internal const int MaximumEntries = 100_000;
    internal const int MaximumCanonicalBytes = 16 * 1024 * 1024;
    internal const int MaximumPathBytes = 4096;
    internal const int MaximumSegmentBytes = 255;

    internal enum Kind : byte { Directory = 1, File = 2, SymbolicLink = 3 }

    internal sealed record Entry(
        string Path, Kind Type, long Length = 0, string? ContentSha256 = null,
        bool Executable = false, string? LinkTarget = null);

    internal sealed record Manifest(string Schema, string IdentitySha256,
        int EntryCount, long FileContentBytes, byte[] CanonicalBytes);

    /// <summary>
    /// Source-order-independent canonical encoding with an explicit domain.
    /// Every candidate is validated before any byte is emitted. Validation
    /// deliberately permits only a conservative ASCII portable subset.
    /// </summary>
    internal static Manifest Build(IReadOnlyList<Entry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0 || source.Count > MaximumEntries)
            throw new InvalidDataException("T4-A invalid tree entry population.");

        var entries = source.ToArray();
        if (entries.Any(x => x is null))
            throw new InvalidDataException("T4-A null tree entry.");
        Array.Sort(entries, (a, b) => string.CompareOrdinal(a.Path, b.Path));

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<string, Entry>(StringComparer.Ordinal);
        long fileBytes = 0;
        foreach (var entry in entries)
        {
            ValidatePath(entry.Path);
            if (!paths.Add(entry.Path))
                throw new InvalidDataException("T4-A duplicate or case-folded path collision.");

            string? parent = Parent(entry.Path);
            if (parent is not null &&
                (!index.TryGetValue(parent, out Entry? parentEntry) ||
                 parentEntry.Type != Kind.Directory))
                throw new InvalidDataException("T4-A missing or non-directory parent.");

            switch (entry.Type)
            {
                case Kind.Directory:
                    if (entry.Length != 0 || entry.ContentSha256 is not null ||
                        entry.Executable || entry.LinkTarget is not null)
                        throw new InvalidDataException("T4-A directory has file/link metadata.");
                    break;
                case Kind.File:
                    if (entry.Length < 0 || entry.LinkTarget is not null ||
                        !ValidSha256(entry.ContentSha256))
                        throw new InvalidDataException("T4-A file metadata is invalid.");
                    fileBytes = checked(fileBytes + entry.Length);
                    break;
                case Kind.SymbolicLink:
                    if (entry.Length != 0 || entry.ContentSha256 is not null ||
                        entry.Executable || entry.LinkTarget is null)
                        throw new InvalidDataException("T4-A symlink has invalid metadata.");
                    ValidatePath(entry.LinkTarget);
                    break;
                default:
                    throw new InvalidDataException("T4-A unsupported tree entry kind.");
            }
            index.Add(entry.Path, entry);
        }

        // Reject dangling, self-referential, chained and ancestor-traversing
        // symbolic links. Target is POSIX-style, relative to the link's parent;
        // no '..' traversal is admitted in this deliberately narrow lab subset.
        foreach (var entry in entries.Where(x => x.Type == Kind.SymbolicLink))
        {
            string parent = Parent(entry.Path) ?? "";
            string resolved = parent.Length == 0 ? entry.LinkTarget! :
                parent + "/" + entry.LinkTarget;
            if (!index.TryGetValue(resolved, out Entry? target) ||
                target.Type == Kind.SymbolicLink)
                throw new InvalidDataException("T4-A symlink target is not a local regular entry.");
        }

        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            // In this laboratory encoding all lengths and integers are LE;
            // the domain has no terminating NUL except the final byte below.
            writer.Write(Encoding.ASCII.GetBytes(Schema));
            writer.Write((byte)0);
            writer.Write(entries.Length);
            foreach (var entry in entries)
            {
                writer.Write((byte)entry.Type);
                WriteAscii(writer, entry.Path);
                if (entry.Type == Kind.File)
                {
                    writer.Write(entry.Length);
                    writer.Write(Convert.FromHexString(entry.ContentSha256!));
                    writer.Write((byte)(entry.Executable ? 1 : 0));
                }
                else if (entry.Type == Kind.SymbolicLink)
                {
                    WriteAscii(writer, entry.LinkTarget!);
                }
                if (bytes.Length > MaximumCanonicalBytes)
                    throw new InvalidDataException("T4-A canonical manifest exceeds research cap.");
            }
        }

        byte[] canonical = bytes.ToArray();
        return new Manifest(Schema, Convert.ToHexStringLower(SHA256.HashData(canonical)),
            entries.Length, fileBytes, canonical);
    }

    private static void WriteAscii(BinaryWriter writer, string value)
    {
        byte[] raw = Encoding.ASCII.GetBytes(value);
        writer.Write(raw.Length);
        writer.Write(raw);
    }

    private static string? Parent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? null : path[..slash];
    }

    private static bool ValidSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;
        foreach (char c in value)
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaximumPathBytes)
            throw new InvalidDataException("T4-A invalid path length.");
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment.Length > MaximumSegmentBytes ||
                segment == "." || segment == ".." ||
                segment[^1] == '.' || segment[^1] == ' ')
                throw new InvalidDataException("T4-A unsafe path component.");
            foreach (char c in segment)
                if (!((c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') ||
                      (c is >= '0' and <= '9') || c is '.' or '-' or '_'))
                    throw new InvalidDataException("T4-A nonportable path character.");
            // Windows device aliases remain reserved even with extensions.
            string stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 &&
                 (stem.StartsWith("COM", StringComparison.Ordinal) ||
                  stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException("T4-A reserved device filename.");
        }
    }
}
