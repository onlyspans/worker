using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Onlyspans.Worker.Api.Configuration;

namespace Onlyspans.Worker.Api.Services;

public sealed class S3SnapshotDownloader(
    IAmazonS3 s3Client,
    S3Options s3Options,
    ILogger<S3SnapshotDownloader> logger
) : ISnapshotDownloader
{
    public async Task<DownloadSnapshotResult> DownloadAsync(
        string snapshotKey,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Downloading snapshot {SnapshotKey} from S3 bucket {Bucket}",
            snapshotKey, s3Options.BucketName);

        var tempPath = Path.Combine(Path.GetTempPath(), $"snapshot_{Guid.NewGuid():N}");

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = s3Options.BucketName,
                Key = snapshotKey
            };

            using var response = await s3Client.GetObjectAsync(request, cancellationToken);

            await response.WriteResponseStreamToFileAsync(tempPath, false, cancellationToken);

            var fileInfo = new FileInfo(tempPath);

            logger.LogInformation("Downloaded snapshot {SnapshotKey} ({SizeBytes} bytes)",
                snapshotKey, fileInfo.Length);

            return new DownloadSnapshotResult(
                FilePath: tempPath,
                SizeBytes: fileInfo.Length,
                LastModified: response.LastModified.HasValue
                    ? new DateTimeOffset(response.LastModified.Value)
                    : DateTimeOffset.UtcNow
            );
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            logger.LogWarning("Snapshot {SnapshotKey} not found in S3", snapshotKey);
            throw new SnapshotNotFoundException(snapshotKey);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "AccessDenied" or "Forbidden")
        {
            logger.LogError("Access denied to snapshot {SnapshotKey}", snapshotKey);
            throw new SnapshotAccessDeniedException(snapshotKey);
        }
    }
}
