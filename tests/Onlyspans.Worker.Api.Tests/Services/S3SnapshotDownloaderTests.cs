using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Services;

namespace Onlyspans.Worker.Api.Tests.Services;

public sealed class S3SnapshotDownloaderTests
{
    private readonly IAmazonS3 _s3Client = Substitute.For<IAmazonS3>();
    private readonly S3Options _options = new() { BucketName = "test-bucket", Region = "us-east-1" };
    private readonly S3SnapshotDownloader _sut;

    public S3SnapshotDownloaderTests()
    {
        _sut = new S3SnapshotDownloader(_s3Client, _options, NullLogger<S3SnapshotDownloader>.Instance);
    }

    [Fact]
    public async Task DownloadAsync_NoSuchKey_ThrowsSnapshotNotFoundException()
    {
        
        var s3Ex = new AmazonS3Exception("Not found") { ErrorCode = "NoSuchKey" };
        _s3Client.GetObjectAsync(Arg.Any<Amazon.S3.Model.GetObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(s3Ex);

        
        var act = () => _sut.DownloadAsync("missing-key", CancellationToken.None);

        
        await act.Should().ThrowAsync<SnapshotNotFoundException>()
            .WithMessage("*missing-key*");
    }

    [Fact]
    public async Task DownloadAsync_AccessDenied_ThrowsSnapshotAccessDeniedException()
    {
        
        var s3Ex = new AmazonS3Exception("Access Denied") { ErrorCode = "AccessDenied" };
        _s3Client.GetObjectAsync(Arg.Any<Amazon.S3.Model.GetObjectRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(s3Ex);

        
        var act = () => _sut.DownloadAsync("restricted-key", CancellationToken.None);

        
        await act.Should().ThrowAsync<SnapshotAccessDeniedException>()
            .WithMessage("*restricted-key*");
    }
}
