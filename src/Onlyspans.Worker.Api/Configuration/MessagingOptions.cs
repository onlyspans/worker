using Strongly.Options;

namespace Onlyspans.Worker.Api.Configuration;

[StronglyOptions(SectionName)]
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public required bool Enabled { get; init; }
    public required KafkaOptions Kafka { get; init; }
}

public sealed class KafkaOptions
{
    public required string BootstrapServers { get; init; }
    public required string Topic { get; init; }
}
