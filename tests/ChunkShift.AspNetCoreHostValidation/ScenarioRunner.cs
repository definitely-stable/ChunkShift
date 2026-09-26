using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ChunkShift;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ChunkShift.AspNetCoreHostValidation;

internal static class ScenarioRunner
{
    internal static async Task RunFastAsync(
        Uri baseUri,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        var passed = new List<string>();

        string health = await client.GetStringAsync(
            new Uri(baseUri, "/health"),
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.Equal("ok", health, "health response");
        passed.Add("real-kestrel-health");

        byte[] payload = PatternData.Create(4 * 1024 * 1024, 0x17A11001u);

        ScanEvidence direct = await PostScanAsync(
            client,
            baseUri,
            "/scan?adapter=body",
            new ByteArrayContent(payload),
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.Equal((long)payload.Length, direct.Bytes, "direct scan byte count");
        ValidationAssert.True(direct.Chunks > 0, "direct scan must emit chunks");
        passed.Add("request-body-direct-scan");

        ScanEvidence oneByte = await PostScanAsync(
            client,
            baseUri,
            "/scan?adapter=body&short=one",
            new ByteArrayContent(payload),
            cancellationToken).ConfigureAwait(false);
        AssertSameSequence(direct, oneByte, "one-byte short reads");
        passed.Add("one-byte-short-read-invariance");

        ScanEvidence randomShort = await PostScanAsync(
            client,
            baseUri,
            "/scan?adapter=body&short=random",
            new ByteArrayContent(payload),
            cancellationToken).ConfigureAwait(false);
        AssertSameSequence(direct, randomShort, "random short reads");
        passed.Add("random-short-read-invariance");

        ScanEvidence bodyReader = await PostScanAsync(
            client,
            baseUri,
            "/scan?adapter=bodyreader",
            new ByteArrayContent(payload),
            cancellationToken).ConfigureAwait(false);
        AssertSameSequence(direct, bodyReader, "BodyReader adapter");
        passed.Add("body-vs-bodyreader-correctness");

        byte[] compressed = CompressGzip(payload);
        using (var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "/scan?adapter=body")))
        {
            request.Content = new ByteArrayContent(compressed);
            request.Content.Headers.ContentType = new("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("gzip");
            using HttpResponseMessage response =
                await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            ScanEvidence decompressed =
                await ReadJsonAsync<ScanEvidence>(response, cancellationToken).ConfigureAwait(false);
            AssertSameSequence(direct, decompressed, "request decompression");
        }
        passed.Add("request-decompression-byte-identity");

        const long requestLimit = 1024 * 1024;
        using (HttpResponseMessage response = await client.PostAsync(
            new Uri(baseUri, $"/scan?limit={requestLimit}"),
            new GeneratedContent(requestLimit + 1, 0x17A11002u),
            cancellationToken).ConfigureAwait(false))
        {
            ValidationAssert.Equal(
                HttpStatusCode.RequestEntityTooLarge,
                response.StatusCode,
                "known-length request over endpoint limit");
        }
        passed.Add("configured-body-limit-known-length");

        using (HttpResponseMessage response = await client.PostAsync(
            new Uri(baseUri, $"/scan?limit={requestLimit}"),
            new GeneratedContent(
                requestLimit + 64 * 1024,
                0x17A11003u,
                publishLength: false),
            cancellationToken).ConfigureAwait(false))
        {
            ValidationAssert.Equal(
                HttpStatusCode.RequestEntityTooLarge,
                response.StatusCode,
                "chunked request over endpoint limit");
        }
        passed.Add("configured-body-limit-while-reading");

        using (var limitRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "/limit-readonly")))
        {
            limitRequest.Content = new ByteArrayContent(new byte[] { 1, 2 });
            using HttpResponseMessage response =
                await client.SendAsync(limitRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using JsonDocument limitJson = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            ValidationAssert.True(
                limitJson.RootElement.GetProperty("isReadOnly").GetBoolean(),
                "request-size feature must become read-only after body reading starts");
        }
        passed.Add("request-limit-locks-after-read");

        ManifestEvidence plain = await PostManifestAsync(
            client,
            baseUri,
            payload,
            includeBlockIndex: false,
            cancellationToken).ConfigureAwait(false);
        ManifestEvidence indexed = await PostManifestAsync(
            client,
            baseUri,
            payload,
            includeBlockIndex: true,
            cancellationToken).ConfigureAwait(false);

        ValidationAssert.Equal(
            plain.ManifestId,
            indexed.ManifestId,
            "BIDX must not alter logical ManifestId");
        ValidationAssert.True(
            !string.Equals(plain.FileDigest, indexed.FileDigest, StringComparison.Ordinal),
            "physical CSM variants must have different FileDigest/ETag");
        passed.Add("logical-vs-physical-csm-identity");

        await ValidateRangesAsync(
            client,
            baseUri,
            indexed,
            cancellationToken).ConfigureAwait(false);
        passed.Add("range-etag-if-range-matrix");

        await ValidateOwnershipAsync(
            client,
            baseUri,
            cancellationToken).ConfigureAwait(false);
        passed.Add("response-stream-pipereader-ownership");

        await ValidateTrickleStreamingAsync(
            client,
            baseUri,
            cancellationToken).ConfigureAwait(false);
        passed.Add("slow-trickle-upload-streams-before-eof");

        await ValidateDisconnectDuringReadAsync(
            client,
            baseUri,
            cancellationToken).ConfigureAwait(false);
        passed.Add("disconnect-during-source-read");

        await ValidateActiveHandlerCancellationAsync(
            client,
            baseUri,
            ignoreCancellation: false,
            cancellationToken).ConfigureAwait(false);
        passed.Add("requestaborted-active-handler-cooperative-cancellation");

        await ValidateActiveHandlerCancellationAsync(
            client,
            baseUri,
            ignoreCancellation: true,
            cancellationToken).ConfigureAwait(false);
        passed.Add("handler-ignore-cancellation-no-preemption");

        await ValidateSlowResponseBackpressureAsync(
            client,
            baseUri,
            cancellationToken).ConfigureAwait(false);
        passed.Add("slow-response-consumer-backpressure");

        await ValidateConcurrentClientsAsync(
            client,
            baseUri,
            cancellationToken).ConfigureAwait(false);
        passed.Add("concurrent-client-isolation");

        var report = new
        {
            schemaVersion = 1,
            purpose = "#17 real-Kestrel functional host validation",
            utc = DateTimeOffset.UtcNow,
            environment = EnvironmentEvidence(),
            baseUri = baseUri.ToString(),
            stableProfileObserved = direct.SequenceSha256,
            scenarios = passed,
        };

        await WriteJsonAsync(outputPath, report, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(report, ValidationJson.Options));
    }

    internal static async Task RunLargeSelfHostedAsync(
        long bytes,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-host-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        WebApplication? app = null;
        try
        {
            app = ValidationHost.Build(
                new HostSettings(
                    "http://127.0.0.1:0",
                    Path.Combine(root, "artifacts"),
                    checked(bytes + 64 * 1024 * 1024)));

            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            Uri baseUri = ResolveAddress(app);

            using var client = CreateClient(timeout: TimeSpan.FromMinutes(30));

            ScanEvidence scan = await PostScanAsync(
                client,
                baseUri,
                "/scan?adapter=body",
                new GeneratedContent(bytes, 0x17A11A01u),
                cancellationToken).ConfigureAwait(false);

            ValidationAssert.Equal(bytes, scan.Bytes, "large scan byte count");

            using HttpResponseMessage manifestResponse = await client.PostAsync(
                new Uri(baseUri, "/manifest?bidx=true"),
                new GeneratedContent(bytes, 0x17A11A01u),
                cancellationToken).ConfigureAwait(false);
            manifestResponse.EnsureSuccessStatusCode();
            ManifestEvidence manifest =
                await ReadJsonAsync<ManifestEvidence>(
                    manifestResponse,
                    cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(bytes, manifest.ContentLength, "large manifest content length");

            byte[] csm = await client.GetByteArrayAsync(
                new Uri(baseUri, manifest.ArtifactPath),
                cancellationToken).ConfigureAwait(false);
            ManifestVerificationResult verified =
                await ChunkManifest.VerifyManifestAsync(
                    new MemoryStream(csm, writable: false),
                    cancellationToken).ConfigureAwait(false);
            ValidationAssert.True(verified.IsValid, "large generated CSM must verify");

            Process process = Process.GetCurrentProcess();
            process.Refresh();

            var report = new
            {
                schemaVersion = 1,
                purpose = "#17 larger-than-RAM real-Kestrel validation",
                utc = DateTimeOffset.UtcNow,
                environment = EnvironmentEvidence(),
                requestedBytes = bytes,
                scan,
                manifest,
                processWorkingSetBytes = process.WorkingSet64,
                processPeakWorkingSetBytes = process.PeakWorkingSet64,
            };

            await WriteJsonAsync(outputPath, report, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(report, ValidationJson.Options));
        }
        finally
        {
            if (app is not null)
            {
                await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    internal static Uri ResolveAddress(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature feature =
            server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose server addresses.");
        string address = feature.Addresses.Single();
        return new Uri(address.EndsWith('/') ? address : address + "/");
    }

    private static HttpClient CreateClient(TimeSpan? timeout = null) =>
        new()
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };

    private static async Task<ScanEvidence> PostScanAsync(
        HttpClient client,
        Uri baseUri,
        string target,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using (content)
        using (HttpResponseMessage response = await client.PostAsync(
            new Uri(baseUri, target.TrimStart('/')),
            content,
            cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            return await ReadJsonAsync<ScanEvidence>(
                response,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<ManifestEvidence> PostManifestAsync(
        HttpClient client,
        Uri baseUri,
        byte[] content,
        bool includeBlockIndex,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsync(
            new Uri(
                baseUri,
                $"/manifest?bidx={includeBlockIndex.ToString().ToLowerInvariant()}".TrimStart('/')),
            new ByteArrayContent(content),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<ManifestEvidence>(
            response,
            cancellationToken).ConfigureAwait(false);
    }

    private static void AssertSameSequence(
        ScanEvidence expected,
        ScanEvidence actual,
        string scenario)
    {
        ValidationAssert.Equal(expected.Chunks, actual.Chunks, $"{scenario} chunk count");
        ValidationAssert.Equal(expected.Bytes, actual.Bytes, $"{scenario} byte count");
        ValidationAssert.Equal(
            expected.SequenceSha256,
            actual.SequenceSha256,
            $"{scenario} sequence digest");
    }

    private static async Task ValidateRangesAsync(
        HttpClient client,
        Uri baseUri,
        ManifestEvidence artifact,
        CancellationToken cancellationToken)
    {
        Uri uri = new(baseUri, artifact.ArtifactPath.TrimStart('/'));
        string expectedEtag = $"\"{artifact.FileDigest}\"";

        using HttpResponseMessage full =
            await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        full.EnsureSuccessStatusCode();
        ValidationAssert.Equal(
            expectedEtag,
            full.Headers.ETag?.ToString() ?? "",
            "strong CSM ETag");
        byte[] fullBytes =
            await full.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        ValidationAssert.Equal(
            artifact.PhysicalLength,
            (long)fullBytes.Length,
            "full CSM length");

        using (var range = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            range.Headers.TryAddWithoutValidation("Range", "bytes=0-99");
            using HttpResponseMessage response =
                await client.SendAsync(range, cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.PartialContent,
                response.StatusCode,
                "prefix range status");
            byte[] bytes =
                await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(100, bytes.Length, "prefix range length");
        }

        using (var suffix = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            suffix.Headers.TryAddWithoutValidation("Range", "bytes=-64");
            using HttpResponseMessage response =
                await client.SendAsync(suffix, cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.PartialContent,
                response.StatusCode,
                "suffix range status");
            byte[] bytes =
                await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(64, bytes.Length, "suffix range length");
        }

        using (var unsatisfiable = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            unsatisfiable.Headers.TryAddWithoutValidation(
                "Range",
                $"bytes={artifact.PhysicalLength + 4096}-");
            using HttpResponseMessage response =
                await client.SendAsync(
                    unsatisfiable,
                    cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.RequestedRangeNotSatisfiable,
                response.StatusCode,
                "unsatisfiable range status");
        }

        using (var matching = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            matching.Headers.TryAddWithoutValidation("Range", "bytes=0-31");
            matching.Headers.TryAddWithoutValidation("If-Range", expectedEtag);
            using HttpResponseMessage response =
                await client.SendAsync(matching, cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.PartialContent,
                response.StatusCode,
                "matching If-Range");
        }

        using (var stale = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            stale.Headers.TryAddWithoutValidation("Range", "bytes=0-31");
            stale.Headers.TryAddWithoutValidation("If-Range", "\"stale-validator\"");
            using HttpResponseMessage response =
                await client.SendAsync(stale, cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.OK,
                response.StatusCode,
                "stale If-Range must fall back to full representation");
            ValidationAssert.Equal(
                artifact.PhysicalLength,
                response.Content.Headers.ContentLength ?? -1,
                "stale If-Range full length");
        }

        using (var conditional = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            conditional.Headers.TryAddWithoutValidation("If-None-Match", expectedEtag);
            using HttpResponseMessage response =
                await client.SendAsync(
                    conditional,
                    cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.NotModified,
                response.StatusCode,
                "matching If-None-Match");
        }
    }

    private static async Task ValidateOwnershipAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        string streamId = Guid.NewGuid().ToString("N");
        byte[] streamBytes = await client.GetByteArrayAsync(
            new Uri(baseUri, $"/ownership/stream/{streamId}".TrimStart('/')),
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.Equal(1024 * 1024, streamBytes.Length, "ownership stream bytes");
        StateSnapshot streamState = await WaitForStateAsync(
            client,
            baseUri,
            streamId,
            state => state.Disposed,
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.True(streamState.Disposed, "Results.Stream must dispose supplied Stream");

        string pipeId = Guid.NewGuid().ToString("N");
        byte[] pipeBytes = await client.GetByteArrayAsync(
            new Uri(baseUri, $"/ownership/pipe/{pipeId}".TrimStart('/')),
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.Equal(1024 * 1024, pipeBytes.Length, "ownership PipeReader bytes");
        StateSnapshot pipeState = await WaitForStateAsync(
            client,
            baseUri,
            pipeId,
            state => state.PipeCompleted,
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.True(
            pipeState.PipeCompleted,
            "Results.Stream must complete supplied PipeReader");
    }

    private static async Task ValidateTrickleStreamingAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        const int total = 2 * 1024 * 1024;
        var (tcp, stream) = await RawHttp.OpenAsync(
            baseUri,
            $"/scan/observe/{id}",
            total,
            cancellationToken).ConfigureAwait(false);
        using (tcp)
        await using (stream)
        {
            byte[] block = PatternData.Create(4 * 1024, 0xA11CE001u);
            int sent = 0;
            while (sent < 512 * 1024)
            {
                await stream.WriteAsync(block, cancellationToken).ConfigureAwait(false);
                sent += block.Length;
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }

            StateSnapshot mid = await WaitForStateAsync(
                client,
                baseUri,
                id,
                state => state.Chunks > 0,
                cancellationToken).ConfigureAwait(false);
            ValidationAssert.True(mid.Bytes < total, "scanner must emit before request EOF");

            while (sent < total)
            {
                int count = Math.Min(block.Length, total - sent);
                await stream.WriteAsync(block.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                sent += count;
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await RawHttp.DrainAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        StateSnapshot completed = await WaitForStateAsync(
            client,
            baseUri,
            id,
            state => state.Completed,
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.True(completed.Succeeded, "trickle upload must complete successfully");
        ValidationAssert.Equal((long)total, completed.Bytes, "trickle uploaded byte count");
    }

    private static async Task ValidateDisconnectDuringReadAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        var (tcp, stream) = await RawHttp.OpenAsync(
            baseUri,
            $"/scan/observe/{id}",
            16 * 1024 * 1024,
            cancellationToken).ConfigureAwait(false);

        byte[] prefix = PatternData.Create(8 * 1024, 0xA11CE002u);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        tcp.Dispose();
        stream.Dispose();

        StateSnapshot state = await WaitForStateAsync(
            client,
            baseUri,
            id,
            value => value.Completed,
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.True(state.RequestAborted, "disconnect must cancel RequestAborted");
        ValidationAssert.Equal(0L, state.Chunks, "incomplete pre-Minimum input must emit no chunk");
        ValidationAssert.True(!state.Succeeded, "disconnect scan must not report success");
    }

    private static async Task ValidateActiveHandlerCancellationAsync(
        HttpClient client,
        Uri baseUri,
        bool ignoreCancellation,
        CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        string target =
            $"/scan/gated/{id}?ignoreCancellation={ignoreCancellation.ToString().ToLowerInvariant()}";
        var (tcp, stream) = await RawHttp.OpenAsync(
            baseUri,
            target,
            16 * 1024 * 1024,
            cancellationToken).ConfigureAwait(false);

        byte[] prefix = PatternData.Create(512 * 1024, 0xA11CE003u);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        _ = await WaitForStateAsync(
            client,
            baseUri,
            id,
            value => value.HandlerEntered,
            cancellationToken).ConfigureAwait(false);

        tcp.Dispose();
        stream.Dispose();

        StateSnapshot aborted = await WaitForStateAsync(
            client,
            baseUri,
            id,
            value => value.RequestAborted,
            cancellationToken).ConfigureAwait(false);

        if (ignoreCancellation)
        {
            ValidationAssert.True(
                aborted.HandlerActive,
                "handler ignoring cancellation must remain active");
            ValidationAssert.True(
                !aborted.Completed,
                "ChunkShift must not preempt an active ignoring handler");

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            StateSnapshot stillBlocked = await GetStateAsync(
                client,
                baseUri,
                id,
                cancellationToken).ConfigureAwait(false);
            ValidationAssert.True(
                stillBlocked.HandlerActive && !stillBlocked.Completed,
                "ignoring handler must remain blocked until application releases it");

            using HttpResponseMessage release = await client.PostAsync(
                new Uri(baseUri, $"/state/{id}/release".TrimStart('/')),
                content: null,
                cancellationToken).ConfigureAwait(false);
            ValidationAssert.Equal(
                HttpStatusCode.NoContent,
                release.StatusCode,
                "gate release status");

            _ = await WaitForStateAsync(
                client,
                baseUri,
                id,
                value => value.Completed,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            StateSnapshot completed = await WaitForStateAsync(
                client,
                baseUri,
                id,
                value => value.Completed,
                cancellationToken).ConfigureAwait(false);
            ValidationAssert.True(
                completed.HandlerCanceled,
                "observing handler must see RequestAborted cancellation");
        }
    }

    private static async Task ValidateSlowResponseBackpressureAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        const long total = 64L * 1024 * 1024;
        var (tcp, stream) = await RawHttp.OpenGetAsync(
            baseUri,
            $"/large-response/{id}?bytes={total}",
            cancellationToken).ConfigureAwait(false);

        using (tcp)
        await using (stream)
        {
            string headers = await RawHttp.ReadHeadersAsync(
                stream,
                cancellationToken).ConfigureAwait(false);
            ValidationAssert.True(
                headers.StartsWith("HTTP/1.1 200", StringComparison.Ordinal),
                "large response must start successfully");

            _ = await WaitForStateAsync(
                client,
                baseUri,
                id,
                state => state.ResponseBytesRead > 0,
                cancellationToken).ConfigureAwait(false);

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            StateSnapshot stalled = await GetStateAsync(
                client,
                baseUri,
                id,
                cancellationToken).ConfigureAwait(false);

            ValidationAssert.True(
                stalled.ResponseBytesRead < total / 2,
                "slow consumer must not cause the full response source to be drained");
        }

        StateSnapshot disposed = await WaitForStateAsync(
            client,
            baseUri,
            id,
            state => state.Disposed,
            cancellationToken).ConfigureAwait(false);
        ValidationAssert.True(disposed.Disposed, "aborted response source must be disposed");
    }

    private static async Task ValidateConcurrentClientsAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        Task<ScanEvidence>[] tasks = Enumerable.Range(0, 4)
            .Select(index => PostScanAsync(
                client,
                baseUri,
                "/scan?adapter=body",
                new GeneratedContent(
                    8 * 1024 * 1024,
                    checked(0xA11CF000u + (uint)index)),
                cancellationToken))
            .ToArray();

        ScanEvidence[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (ScanEvidence result in results)
        {
            ValidationAssert.Equal(
                8L * 1024 * 1024,
                result.Bytes,
                "concurrent scan byte count");
        }

        ValidationAssert.Equal(
            results.Length,
            results.Select(result => result.SequenceSha256).Distinct().Count(),
            "concurrent requests must keep independent chunk identity state");
    }

    private static async Task<StateSnapshot> WaitForStateAsync(
        HttpClient client,
        Uri baseUri,
        string id,
        Func<StateSnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            StateSnapshot? state = await TryGetStateAsync(
                client,
                baseUri,
                id,
                cancellationToken).ConfigureAwait(false);
            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }

        StateSnapshot final = await GetStateAsync(
            client,
            baseUri,
            id,
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Timed out waiting for state {id}: {JsonSerializer.Serialize(final, ValidationJson.Options)}");
    }

    private static async Task<StateSnapshot> GetStateAsync(
        HttpClient client,
        Uri baseUri,
        string id,
        CancellationToken cancellationToken) =>
        await TryGetStateAsync(client, baseUri, id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"State {id} was not found.");

    private static async Task<StateSnapshot?> TryGetStateAsync(
        HttpClient client,
        Uri baseUri,
        string id,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(baseUri, $"/state/{id}".TrimStart('/')),
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<StateSnapshot>(
            response,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(
            ValidationJson.Options,
            cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidDataException($"Response did not contain {typeof(T).Name} JSON.");

    private static byte[] CompressGzip(byte[] content)
    {
        using var destination = new MemoryStream();
        using (var gzip = new GZipStream(
            destination,
            CompressionLevel.Fastest,
            leaveOpen: true))
        {
            gzip.Write(content);
        }

        return destination.ToArray();
    }

    private static object EnvironmentEvidence()
    {
        Process process = Process.GetCurrentProcess();
        return new
        {
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            processId = Environment.ProcessId,
            workingSetBytes = process.WorkingSet64,
            peakWorkingSetBytes = process.PeakWorkingSet64,
        };
    }

    internal static async Task WriteJsonAsync(
        string path,
        object value,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, ValidationJson.Options) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
    }
}
