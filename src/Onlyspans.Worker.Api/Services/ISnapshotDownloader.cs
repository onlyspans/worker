namespace Onlyspans.Worker.Api.Services;

public interface ISnapshotDownloader
{
    Task<DownloadSnapshotResult> DownloadAsync(
        string snapshotKey,
        CancellationToken cancellationToken);
}
