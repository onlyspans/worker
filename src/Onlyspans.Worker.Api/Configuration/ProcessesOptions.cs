using Strongly.Options;

namespace Onlyspans.Worker.Api.Configuration;

[StronglyOptions(SectionName)]
public sealed class ProcessesOptions
{
    public const string SectionName = "Processes";

    public required string Endpoint { get; init; }
}
