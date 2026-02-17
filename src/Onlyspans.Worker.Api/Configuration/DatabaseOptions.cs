using Strongly.Options;

namespace Onlyspans.Worker.Api.Configuration;

[StronglyOptions(SectionName)]
public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public required string Database { get; init; }
}
