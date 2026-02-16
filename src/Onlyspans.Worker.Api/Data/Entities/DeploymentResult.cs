namespace Onlyspans.Worker.Api.Data.Entities;

public class DeploymentResult
{
    public required string DeploymentId { get; set; }
    public required string Status { get; set; }
    public required DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
