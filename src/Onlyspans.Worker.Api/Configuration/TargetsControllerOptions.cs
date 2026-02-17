using Strongly.Options;

namespace Onlyspans.Worker.Api.Configuration;

[StronglyOptions(SectionName)]
public sealed class TargetsControllerOptions
{
    public const string SectionName = "TargetsController";

    public required string Endpoint { get; init; }
}
