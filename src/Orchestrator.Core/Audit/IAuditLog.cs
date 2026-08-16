namespace Orchestrator.Core.Audit;

/// <summary>Append-only audit log.</summary>
/// <remarks>
/// There is deliberately no truncate or delete on this interface. The Python original
/// exposed a reset helper for tests; here tests construct a fresh instance instead, so
/// production code has no way to erase an audit trail.
/// </remarks>
public interface IAuditLog
{
    /// <summary>Append an event and return its id.</summary>
    string Write(
        AuditEventType eventType,
        string tenantId,
        string userId,
        string role,
        IReadOnlyDictionary<string, object?>? details = null);

    /// <summary>
    /// Read the log, optionally scoped to one tenant.
    /// </summary>
    /// <param name="tenantId">
    /// When supplied, only this tenant's events. The API always supplies it from the
    /// authenticated principal — a caller may never read another tenant's trail.
    /// </param>
    IReadOnlyList<AuditEvent> Read(string? tenantId = null);
}
