using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PatchTreeT4C1Probe;

/// <summary>
/// PATCH-TREE-001 T4-C1. Disposable real .NET file-handle/rename probe.
/// NO production publisher, signed update policy, hardened path walker,
/// crash-durable transaction or stable persistent wire-format contract.
/// </summary>
internal static class Program
{
    private const int HeadBytes = 88;
    private const int HeadBodyBytes = 56;
    private const int Replacements = 200;
    private static readonly byte[] Magic = "T4C0v0!\n"u8.ToArray();
    private static readonly byte[] Domain = "chunkshift.t4c0.disposable-tree.v0\0"u8.ToArray();
    private const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string VectorSha = "0a33e8319dd69e441921385b256f5c8333e699f3d8b02af1d0becb3215c4ce67";
    private const string OldHash = "f5c1d9d57e51b275fd1dde57061ba2630f135f886526882173ee0eded280db1c";
    private const string NewHash = "4573b9010344c77e8c3f71d0fdd5561c1d2018a30b4cf29bb4fa08180e7c898d";

    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int OpenDirectory(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "child")
        {
            Child(args[1], args[2], args[3]);
            return 0;
        }
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("Usage: dotnet <dll> --output <evidence.json>");
        string output = Path.GetFullPath(args[1]);
        string temporary = Path.Combine(Path.GetTempPath(), "chunkshift-t4c1-" + Guid.NewGuid().ToString("N"));
        try
        {
            VerifyCodec();
            Directory.CreateDirectory(temporary);
            var results = new Dictionary<string, object?>
            {
                ["schema"] = "chunkshift.patch-tree-t4c1-dotnet-fs.v0",
                ["status"] = "LAB_OBSERVATION_ONLY_NOT_ADOPT",
                ["platform"] = RuntimeInformation.OSDescription,
                ["framework"] = RuntimeInformation.FrameworkDescription,
                ["primitive"] = "System.IO.File.Move(staged,active,overwrite:true)",
                ["writerFlush"] = "FileStream.Flush(flushToDisk:true)",
                ["sourceDestination"] = "SIBLING_FILES_SAME_DIRECTORY",
                ["powerCut"] = "NOT_TESTED",
                ["trustedSignatures"] = "NOT_IMPLEMENTED",
                ["hostileLinksOrReparse"] = "NOT_TESTED",
                ["defaultShareRead"] = ConcurrentCase(Path.Combine(temporary, "default"), FileShare.Read),
                ["explicitShareDelete"] = ConcurrentCase(Path.Combine(temporary, "share-delete"),
                    FileShare.Read | FileShare.Write | FileShare.Delete),
                ["killBefore"] = KillCase(Path.Combine(temporary, "kill-before"), "before"),
                ["killAfter"] = KillCase(Path.Combine(temporary, "kill-after"), "after"),
                ["negatives"] = NegativeCase(Path.Combine(temporary, "negative"))
            };
            WriteJson(output, results);
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        catch (Exception e)
        {
            WriteJson(output, new { schema = "chunkshift.patch-tree-t4c1-dotnet-fs.v0",
                status = "HOST_BLOCKER_UNVERIFIED", exception = e.ToString() });
            Console.Error.WriteLine(e);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    private static void WriteJson(string output, object obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(obj,
            new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException("T4-C1 " + message);
    }

    private static byte[] Head(ulong epoch, ulong sequence, string id)
    {
        Check(id.Length == 64 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            "noncanonical generation ID");
        byte[] head = new byte[HeadBytes];
        Magic.CopyTo(head, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(8), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(16), sequence);
        Convert.FromHexString(id).CopyTo(head, 24);
        SHA256.HashData(head.AsSpan(0, HeadBodyBytes)).CopyTo(head, HeadBodyBytes);
        return head;
    }

    private static (ulong Epoch, ulong Sequence, string Id) Decode(byte[] data)
    {
        Check(data.Length == HeadBytes &&
              data.AsSpan(0, 8).SequenceEqual(Magic) &&
              CryptographicOperations.FixedTimeEquals(data.AsSpan(HeadBodyBytes),
                  SHA256.HashData(data.AsSpan(0, HeadBodyBytes))),
            "missing, torn, incomplete or corrupted head");
        return (BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)),
                BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16)),
                Convert.ToHexStringLower(data.AsSpan(24, 32)));
    }

    private static void VerifyCodec()
    {
        byte[] initial = Head(0, 1, ZeroHash);
        Check(initial.Length == 88 &&
              Convert.ToHexStringLower(initial.AsSpan(56)) == VectorSha,
            "cross-language T4-C0 88-byte head vector diverged");
        Check(Decode(initial) == (0UL, 1UL, ZeroHash), "head decode parity");
        for (int i = 0; i < initial.Length; i++)
        {
            byte[] flipped = (byte[])initial.Clone();
            flipped[i] ^= 1;
            try { Decode(flipped); throw new Exception("Bad head accepted"); }
            catch (InvalidDataException) { /* fail-closed */ }
        }
    }

    private static byte[] FileBytes(string tag, char kind) =>
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat($"{tag}/{kind}\n", 64)));

    private static string Digest(byte[] a, byte[] b)
    {
        using var buffer = new MemoryStream();
        buffer.Write(Domain);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, a.Length);
        buffer.Write(len); buffer.Write(a);
        BinaryPrimitives.WriteInt32LittleEndian(len, b.Length);
        buffer.Write(len); buffer.Write(b);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void SyncedFile(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    private static (string Old, string New) Setup(string root)
    {
        Directory.CreateDirectory(root);
        string versions = Path.Combine(root, "generations");
        Directory.CreateDirectory(versions);
        string Make(string tag)
        {
            byte[] a = FileBytes(tag, 'a'), b = FileBytes(tag, 'b');
            string hash = Digest(a, b);
            string directory = Path.Combine(versions, hash);
            Directory.CreateDirectory(directory);
            SyncedFile(Path.Combine(directory, "a"), a);
            SyncedFile(Path.Combine(directory, "b"), b);
            return hash;
        }
        string old = Make("old"), current = Make("new");
        Check(old == OldHash && current == NewHash, "cross-language generation digest mismatch");
        VerifyGeneration(root, old);
        VerifyGeneration(root, current);
        SyncedFile(Path.Combine(root, "active"), Head(0, 1, old));
        return (old, current);
    }

    private static void VerifyGeneration(string root, string id)
    {
        string directory = Path.Combine(root, "generations", id);
        string a = Path.Combine(directory, "a");
        string b = Path.Combine(directory, "b");
        Check(Directory.Exists(directory) && !File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)
              && File.Exists(a) && File.Exists(b)
              && !File.GetAttributes(a).HasFlag(FileAttributes.ReparsePoint)
              && !File.GetAttributes(b).HasFlag(FileAttributes.ReparsePoint),
            "missing or linked generation content");
        Check(Digest(File.ReadAllBytes(a), File.ReadAllBytes(b)) == id,
            "generation content mismatch");
    }

    private static byte[] ReadHead(string root, FileShare sharing)
    {
        using var stream = new FileStream(Path.Combine(root, "active"),
            FileMode.Open, FileAccess.Read, sharing);
        byte[] bytes = new byte[HeadBytes + 1];
        int n = 0;
        while (n < bytes.Length)
        {
            int read = stream.Read(bytes, n, bytes.Length - n);
            if (read == 0) break;
            n += read;
        }
        return bytes.AsSpan(0, n).ToArray();
    }

    private static (ulong Epoch, ulong Sequence, string Id) Visible(string root,
        FileShare sharing = FileShare.Read | FileShare.Write | FileShare.Delete)
    {
        var head = Decode(ReadHead(root, sharing));
        VerifyGeneration(root, head.Id);
        return head;
    }

    private static string ParentSync(string root)
    {
        if (!OperatingSystem.IsLinux())
            return "NOT_ATTEMPTED_NO_PORTABLE_WINDOWS_DIRECTORY_FSYNC";
        const int O_DIRECTORY_LINUX = 0x10000;
        int fd = OpenDirectory(root, O_DIRECTORY_LINUX);
        if (fd < 0)
            return "UNAVAILABLE_DIRECTORY_OPEN_ERRNO_" + Marshal.GetLastPInvokeError();
        try
        {
            if (Fsync(fd) != 0)
                return "UNAVAILABLE_DIRECTORY_FSYNC_ERRNO_" + Marshal.GetLastPInvokeError();
            return "POSIX_DIRECTORY_FSYNC_SYSCALL_SUCCEEDED";
        }
        finally
        {
            _ = Close(fd);
        }
    }

    private static bool SharingConflict(Exception ex)
    {
        if (!OperatingSystem.IsWindows() || ex is not (IOException or UnauthorizedAccessException))
            return false;
        int code = ex.HResult & 0xFFFF;
        return code is 5 or 32 or 33;
    }

    private static int Publish(string root, string id, ulong sequence,
        Dictionary<string, int>? syncs = null)
    {
        VerifyGeneration(root, id);
        string stage = Path.Combine(root, "stage-" + sequence);
        SyncedFile(stage, Head(0, sequence, id));
        int retries = 0;
        while (true)
        {
            try
            {
                File.Move(stage, Path.Combine(root, "active"), overwrite: true);
                string capability = ParentSync(root);
                if (syncs is not null)
                    syncs[capability] = syncs.GetValueOrDefault(capability) + 1;
                return retries;
            }
            catch (Exception ex) when (SharingConflict(ex) && retries < 500)
            {
                retries++;
                Thread.Sleep(2);
            }
        }
    }

    private static object ConcurrentCase(string root, FileShare sharing)
    {
        var (old, next) = Setup(root);
        int[] counts = new int[4];
        int[] denials = new int[4];
        using var cancel = new CancellationTokenSource();
        var errors = new ConcurrentQueue<string>();
        using var oldPin = new FileStream(Path.Combine(root, "generations", old, "a"),
            FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        byte[] pinned = new byte[checked((int)oldPin.Length)];
        oldPin.ReadExactly(pinned);

        var threads = Enumerable.Range(0, 4).Select(i => new Thread(() =>
        {
            while (!cancel.IsCancellationRequested && errors.IsEmpty)
            {
                (ulong Epoch, ulong Sequence, string Id) head;
                try { head = Decode(ReadHead(root, sharing)); }
                catch (Exception ex) when (SharingConflict(ex))
                {
                    denials[i]++;
                    if (denials[i] > 10000)
                    {
                        errors.Enqueue("reader access-denial limit");
                        cancel.Cancel();
                    }
                    continue;
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"reader-{i} head: {ex}");
                    cancel.Cancel();
                    return;
                }
                try
                {
                    Check(head.Epoch == 0 && head.Sequence is >= 1 and <= 201 &&
                          (head.Id == old || head.Id == next),
                        "unrecognized active cursor or identity");
                    VerifyGeneration(root, head.Id);
                    counts[i]++;
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"reader-{i} generation: {ex}");
                    cancel.Cancel();
                }
            }
        }) { IsBackground = true, Name = "T4C1-reader-" + i }).ToArray();

        var syncs = new Dictionary<string, int>();
        int retries = 0;
        foreach (var thread in threads) thread.Start();
        try
        {
            for (ulong seq = 2; seq <= 201 && errors.IsEmpty; seq++)
            {
                retries += Publish(root, seq % 2 == 0 ? next : old, seq, syncs);
                if (seq % 8 == 0)
                    Thread.Sleep(1);
            }
        }
        finally
        {
            cancel.Cancel();
            foreach (Thread reader in threads)
                Check(reader.Join(TimeSpan.FromSeconds(10)), "stuck reader");
        }

        Check(errors.IsEmpty, "torn/head gap or invalid generation: " +
            string.Join(" | ", errors));
        Check(counts.All(x => x > 0), "reader starvation, zero snapshots");
        Check(Visible(root) == (0UL, 201UL, old), "incorrect final pointer");
        oldPin.Position = 0;
        byte[] after = new byte[pinned.Length];
        oldPin.ReadExactly(after);
        Check(after.AsSpan().SequenceEqual(pinned), "pinned old handle changed");

        return new
        {
            shareMode = sharing.ToString(),
            published = Replacements,
            readerSnapshots = counts,
            readerAccessDenials = denials,
            writerRetries = retries,
            parentSyncResults = syncs,
            oldGenerationPinnedHandleIntact = true,
            observedTornOrMixed = 0,
            readContinuity = denials.Sum() == 0 ? "NO_DENIAL_OBSERVED" :
                "HOST_OBSERVED_SHARING_DENIAL"
        };
    }

    private static void Child(string root, string phase, string id)
    {
        Check(phase is "before" or "after", "bad worker phase");
        VerifyGeneration(root, id);
        string stage = Path.Combine(root, "stage-worker");
        SyncedFile(stage, Head(0, 2, id));
        if (phase == "after")
        {
            // No retry: barrier only applies after a completed direct publication.
            File.Move(stage, Path.Combine(root, "active"), overwrite: true);
            _ = ParentSync(root);
        }
        File.WriteAllText(Path.Combine(root, "worker-" + phase + ".ready"), "ready");
        Thread.Sleep(Timeout.Infinite);
    }

    private static object KillCase(string root, string phase)
    {
        var (old, next) = Setup(root);
        string assembly = Assembly.GetExecutingAssembly().Location;
        Check(File.Exists(assembly), "probe DLL unavailable for child");
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add(assembly);
        psi.ArgumentList.Add("child");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add(phase);
        psi.ArgumentList.Add(next);
        using Process process = Process.Start(psi) ??
            throw new InvalidOperationException("Cannot start .NET child probe.");
        try
        {
            string marker = Path.Combine(root, "worker-" + phase + ".ready");
            var timer = Stopwatch.StartNew();
            while (!File.Exists(marker) && timer.Elapsed < TimeSpan.FromSeconds(18))
            {
                if (process.HasExited)
                    throw new InvalidOperationException("Worker died before barrier " +
                        process.StandardError.ReadToEnd());
                Thread.Sleep(10);
            }
            Check(File.Exists(marker), "child barrier timed out");
            process.Kill(entireProcessTree: true);
            Check(process.WaitForExit(8000), "child did not terminate");
            string expected = phase == "before" ? old : next;
            Check(Visible(root) == (0UL, phase == "before" ? 1UL : 2UL, expected),
                "post-termination active generation/cursor mismatch");
            VerifyGeneration(root, old);
            VerifyGeneration(root, next);
            Check(phase == "after" ||
                  File.Exists(Path.Combine(root, "stage-worker")),
                "uncommitted stage unexpectedly published or lost");
            return new { phase, active = expected, exitCode = process.ExitCode,
                oldAndNewRetained = true, expectedCursor = phase == "before" ? 1 : 2 };
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(8000);
            }
        }
    }

    private static object NegativeCase(string root)
    {
        var (old, next) = Setup(root);
        byte[] valid = Head(0, 1, old);
        int rejected = 0;
        foreach (byte[] test in new[]
        {
            valid.AsSpan(0, 87).ToArray(),
            valid.Concat(new byte[] { 0 }).ToArray(),
            new byte[HeadBytes],
            valid.AsSpan().ToArray()
        })
        {
            if (rejected == 3)
                test[24] ^= 1;
            try { _ = Decode(test); }
            catch (InvalidDataException) { rejected++; }
        }
        Check(rejected == 4, "negative selector corruption escaped");
        // Simulated storage write failure after writing a torn stage:
        string broken = Path.Combine(root, "interrupted-stage");
        SyncedFile(broken, valid.AsSpan(0, 40).ToArray());
        try { _ = Decode(File.ReadAllBytes(broken)); }
        catch (InvalidDataException) { rejected++; }
        Check(rejected == 5, "partial stage was accepted");
        Check(Visible(root).Id == old, "aborted stage changed active pointer");
        File.WriteAllBytes(Path.Combine(root, "generations", next, "a"), "corrupt"u8.ToArray());
        try { VerifyGeneration(root, next); }
        catch (InvalidDataException) { rejected++; }
        Check(rejected == 6, "generation tamper accepted");
        Check(Visible(root) == (0UL, 1UL, old), "uncommitted tamper activated");
        return new { rejected, stageWriteFailureSimulated = true };
    }
}
