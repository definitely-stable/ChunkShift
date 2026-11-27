namespace ChunkShift.Repository.Tests;

public sealed class NativeDirectoryBarrierTests
{
    private static readonly DirectoryBarrierStage[] Order =
    [
        DirectoryBarrierStage.Open,
        DirectoryBarrierStage.ValidateDirectory,
        DirectoryBarrierStage.Flush,
        DirectoryBarrierStage.Close,
    ];

    [Fact]
    public void RealDirectoryBarrierIsOnlyAnObservation()
    {
        using var fixture = new Fixture();
        var attempt = new NativeDirectoryBarrier();
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            Assert.Throws<PlatformNotSupportedException>(() => attempt.Execute(fixture.Root));
            Assert.Equal(DirectoryBarrierState.RejectedBeforeOpen, attempt.State);
            return;
        }

        string sentinel = Path.Combine(fixture.Root, "sentinel");
        using (var file = new FileStream(sentinel, FileMode.CreateNew, FileAccess.Write))
        {
            file.WriteByte(17);
            file.Flush(flushToDisk: true);
        }

        attempt.Execute(fixture.Root);

        Assert.Equal(DirectoryBarrierState.BarrierObserved, attempt.State);
        Assert.Equal(DirectoryBarrierStage.Close, attempt.LastCompleted);
        Assert.Equal(attempt.LastCompleted, attempt.LastAttempted);
        Assert.True(attempt.OwnershipConfirmed && attempt.CloseAttempted && attempt.CloseConfirmed);
        Assert.Equal(new byte[] { 17 }, File.ReadAllBytes(sentinel));
        Assert.Single(Directory.GetFileSystemEntries(fixture.Root));
        Assert.Throws<InvalidOperationException>(() => attempt.Execute(fixture.Root));
    }

    [Fact]
    public void MissingDirectoryRetainsNativeErrorAndDoesNotCreate()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var attempt = new NativeDirectoryBarrier();
        var error = Assert.Throws<NativeDirectoryBarrierException>(() =>
            attempt.Execute(Path.Combine(fixture.Root, "absent")));
        Assert.Equal(OperatingSystem.IsLinux() ? "errno" : "Win32", error.ErrorDomain);
        Assert.Equal(OperatingSystem.IsLinux() ? "open" : "CreateFileW", error.Operation);
        Assert.True(error.NativeError is 2 or 3);
        Assert.Equal(DirectoryBarrierState.FailedAfterOpenAttempt, attempt.State);
        Assert.Equal(DirectoryBarrierStage.Open, attempt.LastAttempted);
        Assert.Equal(DirectoryBarrierStage.None, attempt.LastCompleted);
        Assert.False(attempt.OwnershipConfirmed || attempt.CloseAttempted || attempt.CloseConfirmed);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Fact]
    public void RegularFileNeverReachesDirectoryFlush()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        string file = Path.Combine(fixture.Root, "regular");
        File.WriteAllBytes(file, [13]);
        var attempt = new NativeDirectoryBarrier();
        Exception? error = Record.Exception(() => attempt.Execute(file));
        if (OperatingSystem.IsLinux())
        {
            var native = Assert.IsType<NativeDirectoryBarrierException>(error);
            Assert.Equal(20, native.NativeError); // ENOTDIR
            Assert.Equal("open", native.Operation);
            Assert.False(attempt.OwnershipConfirmed);
        }
        else
        {
            // Windows CreateFile can open regular files with backup semantics.
            Assert.IsType<InvalidDataException>(error);
            Assert.Equal(DirectoryBarrierStage.ValidateDirectory, attempt.LastAttempted);
            Assert.True(attempt.OwnershipConfirmed && attempt.CloseConfirmed);
        }

        Assert.Equal(DirectoryBarrierState.FailedAfterOpenAttempt, attempt.State);
        Assert.Equal(new byte[] { 13 }, File.ReadAllBytes(file));
    }

    [Fact]
    public void ZeroDescriptorIsOwnedAndClosedExactlyOnce()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var model = new RecordingOperations();
        var attempt = new NativeDirectoryBarrier(model);
        attempt.Execute(fixture.Root);
        Assert.Equal(Order, model.Calls);
        Assert.True(attempt.OwnershipConfirmed && attempt.CloseConfirmed);
        Assert.Equal(DirectoryBarrierState.BarrierObserved, attempt.State);
        Assert.Throws<InvalidOperationException>(() => attempt.Execute(fixture.Root));
        Assert.Equal(Order, model.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OperationFailurePreservesErrorAndClosesOnlyKnownHandle(int index)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var primary = new NativeDirectoryBarrierException("injected", "errno", 5);
        var model = new RecordingOperations { Fault = Order[index], Primary = primary };
        var attempt = new NativeDirectoryBarrier(model);
        Assert.Same(primary, Assert.Throws<NativeDirectoryBarrierException>(() => attempt.Execute(fixture.Root)));
        var expected = Order[..(index + 1)].ToList();
        if (index is 1 or 2)
        {
            expected.Add(DirectoryBarrierStage.Close);
        }

        Assert.Equal(expected, model.Calls);
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(index == 0 ? DirectoryBarrierStage.None : Order[index - 1], attempt.LastCompleted);
        Assert.Equal(index != 0, attempt.OwnershipConfirmed);
        Assert.Equal(index != 0, attempt.CloseAttempted);
        Assert.Equal(index is 1 or 2, attempt.CloseConfirmed);
        Assert.Equal(DirectoryBarrierState.FailedAfterOpenAttempt, attempt.State);
        Assert.Throws<InvalidOperationException>(() => attempt.Execute(fixture.Root));
        Assert.Equal(expected, model.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PrimaryAndCloseFaultRetainBothNativeCodes(int index)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        var primary = new NativeDirectoryBarrierException("injected-primary", "errno", 28);
        var cleanup = new NativeDirectoryBarrierException("injected-close", "errno", 5);
        var model = new RecordingOperations { Fault = Order[index], Primary = primary, Cleanup = cleanup };
        var attempt = new NativeDirectoryBarrier(model);
        var observed = Assert.Throws<AggregateException>(() => attempt.Execute(fixture.Root));
        Assert.Collection(observed.InnerExceptions,
            error => Assert.Same(primary, error), error => Assert.Same(cleanup, error));
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(Order[index - 1], attempt.LastCompleted);
        Assert.True(attempt.OwnershipConfirmed && attempt.CloseAttempted);
        Assert.False(attempt.CloseConfirmed);
        Assert.Equal(DirectoryBarrierState.FailedAfterOpenAttempt, attempt.State);
        Assert.Single(model.Calls, stage => stage == DirectoryBarrierStage.Close);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CancellationAfterSynchronousEffectWaitsForClose(int index)
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        var model = new RecordingOperations
        {
            After = stage =>
            {
                if (stage == Order[index])
                {
                    cts.Cancel();
                }
            },
        };
        var attempt = new NativeDirectoryBarrier(model);
        var observed = Assert.ThrowsAny<OperationCanceledException>(() => attempt.Execute(fixture.Root, cts.Token));
        Assert.Equal(cts.Token, observed.CancellationToken);
        Assert.Equal(Order[index], attempt.LastAttempted);
        Assert.Equal(Order[index], attempt.LastCompleted);
        Assert.Equal(DirectoryBarrierState.FailedAfterOpenAttempt, attempt.State);
        Assert.True(attempt.OwnershipConfirmed && attempt.CloseAttempted && attempt.CloseConfirmed);
        var expected = Order[..(index + 1)].ToList();
        if (index != 3)
        {
            expected.Add(DirectoryBarrierStage.Close);
        }

        Assert.Equal(expected, model.Calls);
    }

    [Fact]
    public void PreflightRejectsCancellationRelativePathsAndRoot()
    {
        if (!NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        using var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var model = new RecordingOperations();
        var cancelled = new NativeDirectoryBarrier(model);
        var error = Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Execute(fixture.Root, cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Equal(DirectoryBarrierState.RejectedBeforeOpen, cancelled.State);
        foreach (string path in new[] { "relative", Path.GetPathRoot(fixture.Root)!, " " })
        {
            var attempt = new NativeDirectoryBarrier(model);
            Assert.Throws<ArgumentException>(() => attempt.Execute(path));
            Assert.Equal(DirectoryBarrierState.RejectedBeforeOpen, attempt.State);
            Assert.Equal(DirectoryBarrierStage.None, attempt.LastAttempted);
        }

        Assert.Empty(model.Calls);
    }

    [Fact]
    public void WindowsDeviceAndUncNamespacesAreRejectedBeforeNativeOpen()
    {
        if (!OperatingSystem.IsWindows() || !NativeDirectoryBarrier.IsSupportedPlatform)
        {
            return;
        }

        var model = new RecordingOperations();
        foreach (string path in new[] { @"\\.\C:", @"\\?\C:\private", @"\\server\share\private", @"C:\private:stream" })
        {
            var attempt = new NativeDirectoryBarrier(model);
            Assert.Throws<ArgumentException>(() => attempt.Execute(path));
            Assert.Equal(DirectoryBarrierState.RejectedBeforeOpen, attempt.State);
        }

        Assert.Empty(model.Calls);
    }

    private sealed class RecordingOperations : IDirectoryBarrierOperations
    {
        internal List<DirectoryBarrierStage> Calls { get; } = [];
        internal DirectoryBarrierStage Fault { get; init; }
        internal Exception? Primary { get; init; }
        internal Exception? Cleanup { get; init; }
        internal Action<DirectoryBarrierStage>? After { get; init; }

        public nint Open(string path)
        {
            Record(DirectoryBarrierStage.Open);
            return 0;
        }

        public void ValidateDirectory(nint handle)
        {
            Assert.Equal((nint)0, handle);
            Record(DirectoryBarrierStage.ValidateDirectory);
        }

        public void Flush(nint handle)
        {
            Assert.Equal((nint)0, handle);
            Record(DirectoryBarrierStage.Flush);
        }

        public void Close(nint handle)
        {
            Assert.Equal((nint)0, handle);
            Record(DirectoryBarrierStage.Close);
        }

        private void Record(DirectoryBarrierStage stage)
        {
            Calls.Add(stage);
            if (stage == Fault && Primary is { } primary)
            {
                throw primary;
            }

            if (stage == DirectoryBarrierStage.Close && Cleanup is { } cleanup)
            {
                throw cleanup;
            }

            After?.Invoke(stage);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("chunkshift-directory-barrier-").FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
