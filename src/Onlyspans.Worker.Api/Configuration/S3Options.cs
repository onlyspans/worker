using Strongly.Options;

namespace Onlyspans.Worker.Api.Configuration;

[StronglyOptions(SectionName)]
public sealed class S3Options
{
    public const string SectionName = "S3";

    public required string BucketName { get; init; }
    public required string Region { get; init; }
    public string? Endpoint { get; init; }  // Optional for LocalStack
}
