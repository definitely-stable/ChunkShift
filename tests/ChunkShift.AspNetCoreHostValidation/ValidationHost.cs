using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Cryptography;
using ChunkShift;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace ChunkShift.AspNetCoreHostValidation;

internal static class ValidationHost
{
    internal static WebApplication Build(HostSettings settings)
    {
        Directory.CreateDirectory(settings.ArtifactDirectory);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(settings.Url);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = settings.MaxRequestBodyBytes;
        });
        builder.Services.AddRequestDecompression();

        WebApplication app = builder.Build();
        var state = new ValidationState();

        app.UseRequestDecompression();

        app.MapGet("/health", () => Results.Text("ok"));

        app.MapPost("/scan", async (HttpContext context) =>
        {
            ApplyRequestLimit(context, ResolveLimit(context, settings.MaxRequestBodyBytes));
            string adapter = context.Request.Query["adapter"].FirstOrDefault() ?? "body";
            string shortRead = context.Request.Query["short"].FirstOrDefault() ?? "none";

            ScanEvidence evidence = await ScanAsync(
                context,
                state: null,
                adapter,
                shortRead,
                gateMode: GateMode.None).ConfigureAwait(false);

            return Results.Json(evidence, ValidationJson.Options);
        });

        app.MapPost("/scan/observe/{id}", async (HttpContext context, string id) =>
        {
            ApplyRequestLimit(context, ResolveLimit(context, settings.MaxRequestBodyBytes));
            RequestState requestState = state.GetOrCreate(id);

            ScanEvidence evidence = await ScanAsync(
                context,
                requestState,
                adapter: "body",
                shortRead: "none",
                gateMode: GateMode.None).ConfigureAwait(false);

            return Results.Json(evidence, ValidationJson.Options);
        });

        app.MapPost("/scan/gated/{id}", async (HttpContext context, string id) =>
        {
            ApplyRequestLimit(context, ResolveLimit(context, settings.MaxRequestBodyBytes));
            RequestState requestState = state.GetOrCreate(id);
            bool ignoreCancellation =
                string.Equals(
                    context.Request.Query["ignoreCancellation"].FirstOrDefault(),
                    "true",
                    StringComparison.OrdinalIgnoreCase);

            ScanEvidence evidence = await ScanAsync(
                context,
                requestState,
                adapter: "body",
                shortRead: "none",
                ignoreCancellation ? GateMode.IgnoreCancellation : GateMode.ObserveCancellation)
                .ConfigureAwait(false);

            return Results.Json(evidence, ValidationJson.Options);
        });

        app.MapGet("/state/{id}", (string id) =>
        {
            return state.Requests.TryGetValue(id, out RequestState? requestState)
                ? Results.Json(requestState.Snapshot(), ValidationJson.Options)
                : Results.NotFound();
        });

        app.MapPost("/state/{id}/release", (string id) =>
        {
            if (!state.Requests.TryGetValue(id, out RequestState? requestState))
            {
                return Results.NotFound();
            }

            requestState.Release.TrySetResult();
            return Results.NoContent();
        });

        app.MapPost("/manifest", async (HttpContext context) =>
        {
            ApplyRequestLimit(context, ResolveLimit(context, settings.MaxRequestBodyBytes));
            bool includeBlockIndex =
                string.Equals(
                    context.Request.Query["bidx"].FirstOrDefault(),
                    "true",
                    StringComparison.OrdinalIgnoreCase);

            string temporary = Path.Combine(
                settings.ArtifactDirectory,
                $".{Guid.NewGuid():N}.tmp");

            ManifestInfo info;
            try
            {
                await using (var destination = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous))
                {
                    info = await ChunkManifest.CreateAsync(
                        context.Request.Body,
                        destination,
                        new ManifestCreationOptions
                        {
                            IncludeBlockIndex = includeBlockIndex,
                        },
                        context.RequestAborted).ConfigureAwait(false);

                    await destination.FlushAsync(context.RequestAborted)
                        .ConfigureAwait(false);
                }

                string fileDigest = info.FileDigest.ToHexLower();
                string finalPath = Path.Combine(
                    settings.ArtifactDirectory,
                    $"{fileDigest}.csm");
                File.Move(temporary, finalPath, overwrite: true);

                string manifestId = info.ManifestId.ToString();
                state.Artifacts[fileDigest] =
                    new ArtifactRecord(
                        finalPath,
                        manifestId,
                        fileDigest,
                        info.PhysicalLength);

                return Results.Json(
                    new ManifestEvidence(
                        manifestId,
                        fileDigest,
                        info.PhysicalLength,
                        info.ContentLength,
                        info.ChunkCount,
                        info.HasBlockIndex,
                        $"/artifacts/{fileDigest}.csm"),
                    ValidationJson.Options);
            }
            catch
            {
                File.Delete(temporary);
                throw;
            }
        });

        app.MapGet("/artifacts/{fileDigest}.csm", (HttpContext context, string fileDigest) =>
        {
            if (!state.Artifacts.TryGetValue(fileDigest, out ArtifactRecord? artifact))
            {
                return Results.NotFound();
            }

            context.Response.Headers["X-ChunkShift-Manifest-Id"] = artifact.ManifestId;

            var stream = new FileStream(
                artifact.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.RandomAccess);

            var entityTag =
                new EntityTagHeaderValue($"\"{artifact.FileDigest}\"");

            return Results.Stream(
                stream,
                contentType: "application/vnd.chunkshift.csm",
                entityTag: entityTag,
                enableRangeProcessing: true);
        });

        app.MapGet("/large-response/{id}", (string id, long bytes) =>
        {
            RequestState requestState = state.GetOrCreate(id);
            return Results.Stream(
                new TrackingPatternStream(bytes, requestState),
                contentType: "application/octet-stream");
        });

        app.MapGet("/ownership/stream/{id}", (string id) =>
        {
            RequestState requestState = state.GetOrCreate(id);
            return Results.Stream(
                new TrackingPatternStream(1024 * 1024, requestState),
                contentType: "application/octet-stream");
        });

        app.MapGet("/ownership/pipe/{id}", (string id) =>
        {
            RequestState requestState = state.GetOrCreate(id);
            byte[] payload = PatternData.Create(1024 * 1024, 0xA17E551u);
            var memory = new MemoryStream(payload, writable: false);
            PipeReader reader = PipeReader.Create(
                memory,
                new StreamPipeReaderOptions(leaveOpen: false));
            var tracking = new TrackingPipeReader(reader, requestState);

            return Results.Stream(
                tracking,
                contentType: "application/octet-stream");
        });

        app.MapPost("/limit-readonly", async (HttpContext context) =>
        {
            byte[] one = new byte[1];
            _ = await context.Request.Body.ReadAsync(
                one,
                context.RequestAborted).ConfigureAwait(false);

            IHttpMaxRequestBodySizeFeature? feature =
                context.Features.Get<IHttpMaxRequestBodySizeFeature>();

            return Results.Json(
                new
                {
                    isReadOnly = feature?.IsReadOnly,
                    maxRequestBodySize = feature?.MaxRequestBodySize,
                },
                ValidationJson.Options);
        });

        return app;
    }

    private static long ResolveLimit(HttpContext context, long fallback)
    {
        if (context.Request.Query.TryGetValue("limit", out var values) &&
            long.TryParse(values.FirstOrDefault(), out long value) &&
            value > 0)
        {
            return value;
        }

        return fallback;
    }

    private static void ApplyRequestLimit(HttpContext context, long limit)
    {
        IHttpMaxRequestBodySizeFeature? feature =
            context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = limit;
        }
    }

    private static async Task<ScanEvidence> ScanAsync(
        HttpContext context,
        RequestState? state,
        string adapter,
        string shortRead,
        GateMode gateMode)
    {
        using CancellationTokenRegistration registration =
            state is null
                ? default
                : context.RequestAborted.Register(
                    static value => ((RequestState)value!).MarkAborted(),
                    state);

        Stream source;
        Stream? bodyReaderStream = null;

        if (string.Equals(adapter, "body", StringComparison.OrdinalIgnoreCase))
        {
            source = context.Request.Body;
            adapter = "body";
        }
        else if (string.Equals(adapter, "bodyreader", StringComparison.OrdinalIgnoreCase))
        {
            bodyReaderStream = context.Request.BodyReader.AsStream(leaveOpen: true);
            source = bodyReaderStream;
            adapter = "bodyreader";
        }
        else
        {
            throw new BadHttpRequestException(
                $"Unknown adapter '{adapter}'.",
                StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(shortRead, "none", StringComparison.OrdinalIgnoreCase))
        {
            source = new ShortReadStream(source, shortRead);
        }
        else
        {
            shortRead = "none";
        }

        using var sequence = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        var stopwatch = Stopwatch.StartNew();

        long chunks = 0;
        long bytes = 0;
        double firstChunkMilliseconds = -1;
        int gateEntered = 0;

        try
        {
            await ChunkScanner.ScanAsync(
                source,
                async (chunk, _, cancellationToken) =>
                {
                    if (firstChunkMilliseconds < 0)
                    {
                        firstChunkMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                    }

                    SequenceDigest.Append(sequence, chunk);
                    chunks++;
                    bytes += chunk.Length;
                    state?.AddChunk(chunk.Length);

                    if (state is not null &&
                        gateMode != GateMode.None &&
                        Interlocked.Exchange(ref gateEntered, 1) == 0)
                    {
                        state.MarkHandlerEntered();
                        try
                        {
                            if (gateMode == GateMode.ObserveCancellation)
                            {
                                try
                                {
                                    await state.Release.Task
                                        .WaitAsync(cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    state.MarkHandlerCanceled();
                                    throw;
                                }
                            }
                            else
                            {
                                await state.Release.Task.ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            state.MarkHandlerExited();
                        }
                    }
                },
                cancellationToken: context.RequestAborted).ConfigureAwait(false);

            state?.MarkSucceeded();

            stopwatch.Stop();
            process.Refresh();
            return new ScanEvidence(
                adapter,
                shortRead,
                chunks,
                bytes,
                Convert.ToHexString(sequence.GetHashAndReset()).ToLowerInvariant(),
                stopwatch.Elapsed.TotalSeconds,
                (process.TotalProcessorTime - cpuBefore).TotalSeconds,
                GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore,
                Math.Max(firstChunkMilliseconds, 0),
                process.WorkingSet64,
                process.PeakWorkingSet64);
        }
        catch (Exception exception)
        {
            state?.MarkException(exception);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            state?.MarkCompleted();
            bodyReaderStream?.Dispose();
        }
    }

    private enum GateMode
    {
        None,
        ObserveCancellation,
        IgnoreCancellation,
    }
}
