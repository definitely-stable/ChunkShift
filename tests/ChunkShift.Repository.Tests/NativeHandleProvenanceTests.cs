using System.Buffers.Binary;

namespace ChunkShift.Repository.Tests;

public sealed class NativeHandleProvenanceTests
{
    [Fact]
    public void RealHandlesBindWriterReaderAndParentWithoutPathRootGuess()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        string first = Path.Combine(fixture.Root, "first");
        string second = Path.Combine(fixture.Root, "second");
        NativeHandleIdentity writer;
        using (var handle = File.OpenHandle(first, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            RandomAccess.Write(handle, new byte[] { 1 }, 0);
            writer = NativeHandleProvenance.Observe(handle);
            Assert.False(handle.IsClosed);
        }

        File.WriteAllBytes(second, [1]);
        using var reader = File.OpenHandle(first, FileMode.Open, FileAccess.Read);
        using var other = File.OpenHandle(second, FileMode.Open, FileAccess.Read);
        NativeHandleIdentity read = NativeHandleProvenance.Observe(reader);
        NativeHandleIdentity different = NativeHandleProvenance.Observe(other);
        nint parentHandle = NativeDirectoryOperations.Instance.Open(fixture.Root);
        try
        {
            NativeDirectoryOperations.Instance.ValidateDirectory(parentHandle);
            NativeHandleIdentity parent = NativeHandleProvenance.ObserveOwnedHandle(parentHandle);
            Assert.Equal(HandleObjectKind.Directory, parent.Kind);
            Assert.Equal(HandleObjectKind.RegularFile, writer.Kind);
            Assert.True(writer.SameObject(read));
            Assert.True(writer.SameMountOrVolume(parent));
            Assert.True(writer.SameMountOrVolume(different));
            Assert.False(writer.SameObject(parent));
            Assert.False(writer.SameObject(different));
            Assert.Equal(writer, read);
            Assert.Equal(OperatingSystem.IsLinux() ? HandleIdentityPlatform.Linux : HandleIdentityPlatform.Windows,
                writer.Platform);
            Assert.False(reader.IsClosed);
        }
        finally
        {
            NativeDirectoryOperations.Instance.Close(parentHandle);
        }
    }

    [Fact]
    public void ClosedManagedHandleIsRejected()
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Root, "closed");
        using var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write);
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => NativeHandleProvenance.Observe(handle));
    }

    [Fact]
    public void StatxDecoderReadsExplicitUapiOffsets()
    {
        byte[] bytes = Statx();
        NativeHandleIdentity identity = NativeHandleProvenance.DecodeLinux(bytes);
        Assert.Equal(HandleIdentityPlatform.Linux, identity.Platform);
        Assert.Equal(HandleObjectKind.RegularFile, identity.Kind);
        Assert.Equal(41UL, identity.MountOrVolumeSerial);
        Assert.Equal(259U, identity.DeviceMajor);
        Assert.Equal(3U, identity.DeviceMinor);
        Assert.Equal(0x123456789abcdef0UL, identity.ObjectLow);
        Assert.Equal(0UL, identity.ObjectHigh);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void IncompleteStatxNeverProducesIdentity(int variant)
    {
        byte[] bytes = Statx();
        switch (variant)
        {
            case 0: bytes = bytes[..255]; break;
            case 1: BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x1100); break;
            case 2: BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x1001); break;
            case 3: BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x101); break;
            case 4: BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x2000); break;
            case 5: BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(144), 0); break;
        }

        Assert.Throws<InvalidDataException>(() => NativeHandleProvenance.DecodeLinux(bytes));
    }

    [Fact]
    public void WindowsVolumeGuidIsParsedOnlyFromNativeGuidNamespace()
    {
        const string expected = "6f021ef0-0000-0000-0000-100000000000";
        Assert.Equal(Guid.Parse(expected), NativeHandleProvenance.ParseVolumeGuid(
            @"\\?\Volume{6F021EF0-0000-0000-0000-100000000000}\private\file"));
    }

    [Theory]
    [InlineData(@"C:\private\file")]
    [InlineData(@"\\server\share\file")]
    [InlineData(@"\\?\Volume{6f021ef0-0000-0000-0000-100000000000}")]
    [InlineData(@"\\?\Volume{6f021ef0-0000-0000-0000-100000000000}x")]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\file")]
    [InlineData(@"\\?\Volume{invalid}\file")]
    public void NoVolumeGuidFallback(string path) =>
        Assert.Throws<InvalidDataException>(() => NativeHandleProvenance.ParseVolumeGuid(path));

    [Fact]
    public void IdentityComparisonRejectsCrossMountAndCrossVolumeAliases()
    {
        NativeHandleIdentity linux = NativeHandleProvenance.DecodeLinux(Statx());
        Assert.True(linux.SameObject(linux));
        Assert.False(linux.SameObject(linux with { MountOrVolumeSerial = 42 }));
        Assert.False(linux.SameObject(linux with { DeviceMinor = 4 }));
        Assert.False(linux.SameObject(linux with { ObjectLow = 17 }));
        Assert.False(linux.SameObject(linux with { Kind = HandleObjectKind.Directory }));
        Assert.False(linux.SameMountOrVolume(default));
        Assert.False(default(NativeHandleIdentity).SameObject(default));
        var windows = new NativeHandleIdentity(HandleIdentityPlatform.Windows, HandleObjectKind.RegularFile,
            41, 0, 0, Guid.Parse("6f021ef0-0000-0000-0000-100000000000"), 17, 19);
        Assert.True(windows.SameObject(windows));
        Assert.False(windows.SameMountOrVolume(linux));
        Assert.False(windows.SameObject(windows with { ObjectHigh = 20 }));
        Assert.False(windows.SameObject(windows with { MountOrVolumeSerial = 42 }));
        Assert.False(windows.SameObject(windows with { VolumeGuid = Guid.NewGuid() }));
        Assert.False(windows.SameObject(windows with { VolumeGuid = Guid.Empty }));
    }

    private static byte[] Statx()
    {
        byte[] bytes = new byte[256];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x1101);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x81a4);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 0x123456789abcdef0UL);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(136), 259);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(140), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(144), 41);
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-handle-identity-").FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
