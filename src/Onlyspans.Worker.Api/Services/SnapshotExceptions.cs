namespace Onlyspans.Worker.Api.Services;

public sealed class SnapshotNotFoundException(string snapshotKey)
    : Exception($"Snapshot not found: {snapshotKey}");

public sealed class SnapshotAccessDeniedException(string snapshotKey)
    : Exception($"Access denied to snapshot: {snapshotKey}");
