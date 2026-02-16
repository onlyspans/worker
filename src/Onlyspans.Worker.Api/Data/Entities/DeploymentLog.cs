namespace Onlyspans.Worker.Api.Data.Entities;

public class DeploymentLog
{
    public Guid Id { get; init; }
    public required string DeploymentId { get; set; }
    public required DateTime Timestamp { get; set; }
    public required string LogLevel { get; set; }
    public required string Message { get; set; }
    public string? Source { get; set; }
}
