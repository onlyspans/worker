namespace Onlyspans.Worker.Api.Messages;

public record DeploymentLogMessage(
    string DeploymentId,
    DateTimeOffset Timestamp,
    string LogLevel,
    string Message
);
