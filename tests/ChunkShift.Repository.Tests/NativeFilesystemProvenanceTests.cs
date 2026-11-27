namespace ChunkShift.Repository.Tests;

public sealed class NativeFilesystemProvenanceTests
{
    private static readonly NativeHandleIdentity LinuxIdentity = new(
        HandleIdentityPlatform.Linux, HandleObjectKind.RegularFile, 41, 259, 3, Guid.Empty, 17, 0);

    private const string Row = "41 20 259:3 / /private rw,nosuid shared:1 - ext4 /dev/sda rw,data=writeback,nobarrier\n";

    [Fact]
    public void MountIdSelectsBindMountNotDeviceOrPath()
    {
        string snapshot = "40 20 259:3 / /private rw - ext4 /dev/sda rw\n" +
            "41 20 259:3 /a\\040b /other\\134dir ro,nosuid shared:1 future:tag - ext4 /dev/sda rw,data=writeback\n";
        NativeFilesystemObservation result = NativeFilesystemProvenance.ParseLinuxMountInfo(snapshot, LinuxIdentity, out string row);
        Assert.Equal(LinuxIdentity, result.Identity);
        Assert.Equal("ext4", result.FilesystemType);
        Assert.Equal("ro,nosuid", result.MountOptions);
        Assert.Equal("rw,data=writeback", result.SuperblockOptions);
        Assert.StartsWith("41 ", row, StringComparison.Ordinal);
        Assert.Null(result.WindowsFilesystemFlags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("40 20 259:3 / /private rw - ext4 /dev/sda rw\n")]
    [InlineData("41 20 259:4 / /private rw - ext4 /dev/sda rw\n")]
    [InlineData("41 20 259:3 / /private rw ext4 /dev/sda rw\n")]
    [InlineData("41 20 259:3 / /private rw - ext4 /dev/sda rw")]
    [InlineData("41 20 259:3 / /private rw - ext4  /dev/sda rw\n")]
    [InlineData("41 20 259:3 / /private rw - ext4 /dev/sda rw extra\n")]
    [InlineData("41 20 259:3 / /private rw - ext4 /dev/sda rw\r\n")]
    [InlineData("41 20 259:3 / /private rw - ext4 /dev/sda rw\n41 20 259:3 / /other ro - ext4 /dev/sda rw\n")]
    [InlineData("41 20 4294967296:3 / /private rw - ext4 /dev/sda rw\n")]
    [InlineData("41 20 259:3 / /private rw - ext4 /dev/sda rw\nbad\n")]
    public void MalformedMissingOrAmbiguousMountIsRejected(string snapshot) =>
        Assert.Throws<InvalidDataException>(() =>
            NativeFilesystemProvenance.ParseLinuxMountInfo(snapshot, LinuxIdentity, out _));

    [Fact]
    public void InvalidIdentityAndOversizedRowsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            NativeFilesystemProvenance.ParseLinuxMountInfo(Row, default, out _));
        Assert.Throws<InvalidDataException>(() =>
            NativeFilesystemProvenance.ParseLinuxMountInfo(new string('x', 65537) + "\n", LinuxIdentity, out _));
    }

    [Fact]
    public void ReadIsBoundedAndDoesNotOwnInput()
    {
        int limit = NativeFilesystemProvenance.MaximumMountInfoBytes;
        using var exact = new MemoryStream(new byte[limit]);
        Assert.Equal(limit, NativeFilesystemProvenance.ReadBoundedMountInfo(exact).Length);
        Assert.True(exact.CanRead);
        using var excessive = new MemoryStream(new byte[limit + 20]);
        Assert.Throws<InvalidDataException>(() => NativeFilesystemProvenance.ReadBoundedMountInfo(excessive));
        Assert.Equal(limit + 1L, excessive.Position);
        Assert.True(excessive.CanRead);
    }

    [Fact]
    public void RealFileAndParentHandlesObserveFilesystemWithoutClosing()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        string root = Directory.CreateTempSubdirectory("chunkshift-filesystem-").FullName;
        try
        {
            string file = Path.Combine(root, "file");
            using var writer = File.OpenHandle(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            RandomAccess.Write(writer, new byte[] { 1 }, 0);
            NativeFilesystemObservation first = NativeFilesystemProvenance.Observe(writer);
            Assert.False(writer.IsClosed);
            Assert.Equal(NativeHandleProvenance.Observe(writer), first.Identity);
            Assert.False(string.IsNullOrWhiteSpace(first.FilesystemType));
            nint parent = NativeDirectoryOperations.Instance.Open(root);
            try
            {
                NativeFilesystemObservation directory = NativeFilesystemProvenance.ObserveOwnedHandle(parent);
                Assert.True(first.Identity.SameMountOrVolume(directory.Identity));
                Assert.Equal(HandleObjectKind.Directory, directory.Identity.Kind);
                Assert.Equal(first.FilesystemType, directory.FilesystemType);
                if (OperatingSystem.IsLinux())
                {
                    Assert.NotNull(first.MountOptions);
                    Assert.NotNull(first.SuperblockOptions);
                    Assert.Null(first.WindowsFilesystemFlags);
                }
                else
                {
                    Assert.Null(first.MountOptions);
                    Assert.NotNull(first.WindowsVolumeSerial);
                    Assert.NotNull(first.WindowsFilesystemFlags);
                    Assert.True(first.WindowsMaximumComponentLength > 0);
                    Assert.Equal(first.WindowsVolumeSerial, directory.WindowsVolumeSerial);
                }
            }
            finally
            {
                NativeDirectoryOperations.Instance.Close(parent);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DisposedHandleCannotBeObserved()
    {
        string root = Directory.CreateTempSubdirectory("chunkshift-filesystem-").FullName;
        try
        {
            using var handle = File.OpenHandle(Path.Combine(root, "file"), FileMode.CreateNew, FileAccess.Write);
            handle.Dispose();
            Assert.Throws<ObjectDisposedException>(() => NativeFilesystemProvenance.Observe(handle));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
