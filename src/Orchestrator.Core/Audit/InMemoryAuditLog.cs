namespace Orchestrator.Core.Audit;

/// <summary>
/// In-memory audit log. Append-only within a process; no database, by design.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton and therefore shared across concurrent requests — which the
/// single-threaded Python original never had to survive. Appends and reads take the same
/// lock, so ids stay gap-free and monotonic and a reader never observes a torn list.
/// </para>
/// <para>
/// A real audit store must additionally be tamper-evident: WORM storage or a per-tenant hash
/// chain, so an event cannot be edited or removed without detection.
/// </para>
/// </remarks>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly Lock _gate = new();
    private readonly List<AuditEvent> _events = [];
    private int _counter;

    /// <inheritdoc/>
    public string Write(
        AuditEventType eventType,
        string tenantId,
        string userId,
        string role,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        lock (_gate)
        {
            var eventId = $"evt-{++_counter:D6}";
            _events.Add(new AuditEvent
            {
                EventId = eventId,
                Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                EventType = eventType,
                TenantId = tenantId,
                UserId = userId,
                Role = role,
                Details = details is null
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?>(details),
            });
            return eventId;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<AuditEvent> Read(string? tenantId = null)
    {
        lock (_gate)
        {
            return tenantId is null
                ? [.. _events]
                : _events.Where(e => e.TenantId == tenantId).ToList();
        }
    }
}
