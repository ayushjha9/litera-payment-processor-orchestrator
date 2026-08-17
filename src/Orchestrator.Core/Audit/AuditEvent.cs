namespace Orchestrator.Core.Audit;

/// <summary>What kind of thing happened.</summary>
public enum AuditEventType
{
    /// <summary>
    /// Written when the workflow is invoked, before any evidence is read, so a crash
    /// mid-run still leaves evidence that the run happened.
    /// </summary>
    WorkflowRun,

    /// <summary>Written before the risky action is attempted, allowed or blocked.</summary>
    ActionAttempt,

    /// <summary>Written at the end of every run with the recommendation actually returned.</summary>
    Decision,
}

/// <summary>
/// One append-only audit record.
/// </summary>
/// <remarks>
/// <see cref="Details"/> carries document <b>ids</b>, never document text — untrusted vendor
/// prose must not propagate into logs that humans and downstream tools read.
/// </remarks>
public sealed record AuditEvent
{
    public required string EventId { get; init; }

    /// <summary>ISO-8601 UTC.</summary>
    public required string Timestamp { get; init; }

    public required AuditEventType EventType { get; init; }

    public required string TenantId { get; init; }

    public required string UserId { get; init; }

    public required string Role { get; init; }

    public IReadOnlyDictionary<string, object?> Details { get; init; } =
        new Dictionary<string, object?>();
}
