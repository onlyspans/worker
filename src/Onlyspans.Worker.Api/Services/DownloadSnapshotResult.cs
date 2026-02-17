namespace Onlyspans.Worker.Api.Services;

public sealed record DownloadSnapshotResult(
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastModified
);
