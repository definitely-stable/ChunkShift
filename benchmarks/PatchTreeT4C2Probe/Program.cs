using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PatchTreeT4C2Probe;

/// <summary>
/// PATCH-TREE-001 T4-C2A: scoped, deliberately incomplete filesystem fault lab.
/// Not a production publisher; no hostile path race, authenticated witness or
/// power-off durability is implemented.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] Magic = "T4C0v0!\n"u8.ToArray();
    private const int HeadLength = 88;
    private const int BodyLength = 56;
    private const string HeadVector = "0a33e8319dd69e441921385b256f5c8333e699f3d8b02af1d0becb3215c4ce67";

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenDirectory(byte[] pathname, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    private enum Cut
    {
        BeforeStage,
        PartialStage,
        AfterStageFlush,
        CorruptStage,
        BeforeMove,
        AfterMoveBeforeSync,
        ParentSyncFailure,
        AfterSyncBeforeAck,
        Complete
    }

    private sealed record Fixture(string Root, string Old, string Target);

    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "lock-child")
        {
            using var guard = LockWriter(args[1]);
            File.WriteAllText(args[2], "held");
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("Expected --output <evidence.json>");

        string output = Path.GetFullPath(args[1]);
        string temp = Path.Combine(Path.GetTempPath(), "chunkshift-t4c2-" + Guid.NewGuid().ToString("N"));
        try
        {
            TestCodec();
            Directory.CreateDirectory(temp);
            var observations = Enum.GetValues<Cut>().Select(cut =>
                RunCut(Path.Combine(temp, "cut-" + cut), cut)).ToArray();
            object negative = NegativePathsAndTargets(Path.Combine(temp, "negatives"));
            object lockTest = CompetingProcessLock(Path.Combine(temp, "lock-case"));
            var evidence = new
            {
                schema = "chunkshift.patch-tree-t4c2a-faults.v0",
                status = "LAB_FAULT_OBSERVATION_ONLY_NOT_ADOPT",
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                publication = "File.Move(stage,active,overwrite:true):SAME_DIRECTORY_ONLY",
                stageFlush = "FileStream.Flush(flushToDisk:true)",
                linuxDirSync = "libc.fsync(dirfd):ATTEMPTED_IF_COMMITTED",
                windowsDirSync = "NOT_ATTEMPTED_NOT_DURABILITY_EVIDENCE",
                trustedPolicy = "NOT_IMPLEMENTED_RFC0006_HOST_OWNED",
                powerCut = "NOT_TESTED",
                hostilePathRace = "NOT_TESTED",
                cuts = observations,
                negatives = negative,
                processLock = lockTest
            };
            WriteJson(output, evidence);
            Console.WriteLine(File.ReadAllText(output));
            return 0;
        }
        catch (Exception e)
        {
            WriteJson(output, new { schema = "chunkshift.patch-tree-t4c2a-faults.v0",
                status = "HOST_BLOCKER_UNVERIFIED", error = e.ToString() });
            Console.Error.WriteLine(e);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static void Check(bool okay, string why)
    {
        if (!okay) throw new InvalidDataException("T4-C2A " + why);
    }

    private static void WriteJson(string path, object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static byte[] Encode(ulong sequence, string id)
    {
        byte[] bytes = new byte[HeadLength];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), sequence);
        Convert.FromHexString(id).CopyTo(bytes, 24);
        SHA256.HashData(bytes.AsSpan(0, BodyLength)).CopyTo(bytes, BodyLength);
        return bytes;
    }

    private static (ulong Sequence, string Id) Decode(byte[] bytes)
    {
        Check(bytes.Length == HeadLength && bytes.AsSpan(0, 8).SequenceEqual(Magic)
            && CryptographicOperations.FixedTimeEquals(bytes.AsSpan(BodyLength),
                SHA256.HashData(bytes.AsSpan(0, BodyLength))),
            "partial or invalid active pointer");
        Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)) == 0,
            "unexpected epoch");
        ulong seq = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16));
        Check(seq > 0, "zero sequence");
        return (seq, Convert.ToHexStringLower(bytes.AsSpan(24, 32)));
    }

    private static void TestCodec()
    {
        string zeros = new('0', 64);
        byte[] head = Encode(1, zeros);
        Check(Convert.ToHexStringLower(head.AsSpan(BodyLength)) == HeadVector &&
            Decode(head) == (1UL, zeros), "T4C1/T4C0 fixture drift");
        foreach (int index in Enumerable.Range(0, HeadLength))
        {
            byte[] broken = (byte[])head.Clone();
            broken[index] ^= 1;
            bool rejected = false;
            try { Decode(broken); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "one-bit pointer mutation was accepted");
        }
    }

    private static void SyncedFile(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }

    private static FileStream LockWriter(string root) =>
        new(Path.Combine(root, "publisher.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

    private static string HashFile(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    private static Fixture Make(string root)
    {
        Directory.CreateDirectory(root);
        string generations = Path.Combine(root, "generations");
        Directory.CreateDirectory(generations);

        string Seed(string content)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(content);
            string id = Convert.ToHexStringLower(SHA256.HashData(bytes));
            string dir = Path.Combine(generations, id);
            Directory.CreateDirectory(dir);
            SyncedFile(Path.Combine(dir, "payload"), bytes);
            return id;
        }
        string old = Seed("T4C2:original:immutable\n");
        string target = Seed("T4C2:target:immutable\n");
        SyncedFile(Path.Combine(root, "active"), Encode(1, old));
        return new Fixture(root, old, target);
    }

    private static void VerifyGeneration(Fixture fixture, string id)
    {
        Check(id == fixture.Old || id == fixture.Target, "unknown generation");
        string dir = Path.Combine(fixture.Root, "generations", id);
        // This is a cheap preflight, NOT race-proof: a malicious actor can
        // substitute a parent between GetAttributes and later File.Open.
        Check(!File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint),
            "linked generation parent");
        string payload = Path.Combine(dir, "payload");
        Check(!File.GetAttributes(payload).HasFlag(FileAttributes.ReparsePoint),
            "linked payload");
        Check(HashFile(payload) == id, "wrong generation bytes");
    }

    private static (ulong Sequence, string Id) Visible(Fixture f)
    {
        using var fs = new FileStream(Path.Combine(f.Root, "active"),
            FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Write | FileShare.Delete);
        byte[] bytes = new byte[HeadLength + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int n = fs.Read(bytes, count, bytes.Length - count);
            if (n == 0) break;
            count += n;
        }
        var head = Decode(bytes.AsSpan(0, count).ToArray());
        VerifyGeneration(f, head.Id);
        return head;
    }

    private static string ParentSync(string root)
    {
        if (!OperatingSystem.IsLinux())
            return "NOT_ATTEMPTED_WINDOWS_NO_VALIDATED_DIR_SYNC";
        const int O_DIRECTORY_LINUX = 0x10000;
        int fd = OpenDirectory(Encoding.UTF8.GetBytes(root + "\0"), O_DIRECTORY_LINUX);
        if (fd < 0)
            return "UNAVAILABLE_OPEN_ERRNO_" + Marshal.GetLastPInvokeError();
        try
        {
            return Fsync(fd) == 0 ? "LINUX_DIR_FSYNC_SYSCALL_OK"
                : "UNAVAILABLE_FSYNC_ERRNO_" + Marshal.GetLastPInvokeError();
        }
        finally { _ = Close(fd); }
    }

    private static void CheckSameParent(string root, string stage, string active)
    {
        string resolved = Path.GetFullPath(root);
        StringComparison comparison = OperatingSystem.IsWindows() ?
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Check(string.Equals(Path.GetDirectoryName(Path.GetFullPath(stage)), resolved, comparison)
            && string.Equals(Path.GetDirectoryName(Path.GetFullPath(active)), resolved, comparison),
            "cross-parent or external staged selector rejected");
        Check(!File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint),
            "linked publication parent");
    }

    private static object RunCut(string root, Cut cut)
    {
        Fixture f = Make(root);
        string stage = Path.Combine(root, "active.stage");
        string active = Path.Combine(root, "active");
        CheckSameParent(root, stage, active);
        using var writer = LockWriter(root);
        var original = Visible(f);
        Check(original == (1UL, f.Old), "incorrect initial state");
        VerifyGeneration(f, f.Target);
        string sync = "NOT_REACHED";
        string outcome;
        try
        {
            if (cut == Cut.BeforeStage)
                throw new IOException("LAB_INJECTED_BEFORE_STAGE");
            using (var file = new FileStream(stage, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                byte[] head = Encode(2, f.Target);
                if (cut == Cut.PartialStage)
                {
                    file.Write(head.AsSpan(0, 40));
                    throw new IOException("LAB_INJECTED_PARTIAL_STAGE_WRITE");
                }
                file.Write(head);
                file.Flush(flushToDisk: true);
            }
            if (cut == Cut.AfterStageFlush)
                throw new IOException("LAB_INJECTED_AFTER_FLUSH");
            if (cut == Cut.CorruptStage)
                File.WriteAllBytes(stage, new byte[16]);
            byte[] staged = File.ReadAllBytes(stage);
            Check(Decode(staged) == (2UL, f.Target),
                "invalid stage must not be published");
            Check(Visible(f) == original, "old head changed before commit");
            if (cut == Cut.BeforeMove)
                throw new IOException("LAB_INJECTED_BEFORE_MOVE");
            // The only active publication mutation of this lab.
            File.Move(stage, active, overwrite: true);
            if (cut == Cut.AfterMoveBeforeSync)
                throw new IOException("LAB_INJECTED_AFTER_MOVE_BEFORE_SYNC");
            if (cut == Cut.ParentSyncFailure)
                throw new IOException("LAB_INJECTED_PARENT_SYNC_FAILURE");
            sync = ParentSync(root);
            if (cut == Cut.AfterSyncBeforeAck)
                throw new IOException("LAB_INJECTED_ACK_LOSS");
            outcome = "RETURNED_AFTER_SYNCCALL";
        }
        catch (IOException ex) when (ex.Message.StartsWith("LAB_INJECTED_", StringComparison.Ordinal))
        {
            outcome = ex.Message;
        }
        catch (InvalidDataException ex) when (cut == Cut.CorruptStage &&
            ex.Message == "T4-C2A partial or invalid active pointer")
        {
            outcome = "LAB_REJECTED_CORRUPT_STAGED_POINTER";
        }
        bool shouldBeNew = cut is Cut.AfterMoveBeforeSync or Cut.ParentSyncFailure or
            Cut.AfterSyncBeforeAck or Cut.Complete;
        var final = Visible(f);
        Check(final.Sequence == (shouldBeNew ? 2UL : 1UL) &&
              final.Id == (shouldBeNew ? f.Target : f.Old),
            "real filesystem recovery chose incorrect generation");
        VerifyGeneration(f, f.Old);
        VerifyGeneration(f, f.Target);
        bool stageRemains = File.Exists(stage);
        Check(stageRemains == (!shouldBeNew && cut != Cut.BeforeStage),
            "unexpected staged selector retention or publication");
        bool observedDirectorySyncCall =\n            (cut is Cut.AfterSyncBeforeAck or Cut.Complete) &&\n            sync == "LINUX_DIR_FSYNC_SYSCALL_OK";
        return new
        {
            cut = cut.ToString(), phaseOutcome = outcome,
            visible = shouldBeNew ? "NEW_VALID_GENERATION" : "OLD_VALID_GENERATION",
            postCommit = shouldBeNew,
            sync,
            directorySyncSystemCallObserved = observedDirectorySyncCall,
            claimPowerCutDurability = false,
            stageRemains,
            generationsRetained = true
        };
    }

    private static object NegativePathsAndTargets(string root)
    {
        Fixture f = Make(root);
        string outside = Path.Combine(Path.GetDirectoryName(root)!, "outside-active");
        int rejects = 0;
        try { CheckSameParent(root, outside, Path.Combine(root, "active")); }
        catch (InvalidDataException) { rejects++; }
        try { CheckSameParent(root, Path.Combine(root, "..", "escape"),
                  Path.Combine(root, "active")); }
        catch (InvalidDataException) { rejects++; }
        try { CheckSameParent(root, Path.Combine(root, "active.stage"),
                  Path.Combine(root, "..", "escape")); }
        catch (InvalidDataException) { rejects++; }
        string corrupt = Path.Combine(root, "generations", f.Target, "payload");
        File.WriteAllText(corrupt, "modified-before-publish");
        try { VerifyGeneration(f, f.Target); }
        catch (InvalidDataException) { rejects++; }
        Check(rejects == 4, "negative path/target checks");
        Check(Visible(f) == (1UL, f.Old), "negative test changed active generation");
        return new { rejected = rejects, previousGenerationUnaffected = true,
            precheckIsRaceProof = false, crossVolumeAttempted = false };
    }

    private static object CompetingProcessLock(string root)
    {
        Fixture f = Make(root);
        string marker = Path.Combine(root, "child-owns-lock");
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardError = true
        };
        psi.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        psi.ArgumentList.Add("lock-child");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add(marker);
        using Process child = Process.Start(psi) ??
            throw new InvalidOperationException("cannot start lock child");
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(marker) && timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (child.HasExited)
                    throw new InvalidOperationException("lock child failed: " +
                        child.StandardError.ReadToEnd());
                Thread.Sleep(10);
            }
            Check(File.Exists(marker), "lock child never acquired token");
            bool denied = false;
            try
            {
                using var competing = LockWriter(root);
            }
            catch (IOException) { denied = true; }
            catch (UnauthorizedAccessException) { denied = true; }
            Check(denied, "second writer could acquire same advisory .NET file share lock");
            child.Kill(entireProcessTree: true);
            Check(child.WaitForExit(8000), "lock child did not stop");
            using var acquired = LockWriter(root);
            Check(Visible(f) == (1UL, f.Old), "lock experiment published unexpectedly");
            return new { competingWriterDenied = true, reacquiredAfterChildExit = true,
                processLockIsSecurityBoundary = false };
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(8000);
            }
        }
    }
}
