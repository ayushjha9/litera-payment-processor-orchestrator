using Orchestrator.Core.Audit;
using Orchestrator.Core.Models;

namespace Orchestrator.Api.Contracts;

/// <summary>A quoted excerpt backing a stated reason.</summary>
public sealed record CitationResponse(string DocumentId, string Snippet);

/// <summary>
/// The workflow's answer.
/// </summary>
/// <remarks>
/// A blocked action still returns <c>200</c>. <c>blocked_pending_approval</c> is a successful
/// assessment whose answer is "no" — it carries the reasons, citations and audit ids the
/// caller needs. A <c>403</c> would throw that body away and invite callers to treat a
/// policy decision as a transport error.
/// </remarks>
public sealed record WorkflowResponse(
    RiskLevel RiskLevel,
    string Recommendation,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<CitationResponse> Citations,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresApproval,
    ActionStatus ActionStatus,
    IReadOnlyList<string> AuditEventIds)
{
    /// <summary>Project the domain result onto the wire contract.</summary>
    public static WorkflowResponse From(WorkflowResult result) => new(
        result.RiskLevel,
        result.Recommendation,
        result.Reasons,
        [.. result.Citations.Select(c => new CitationResponse(c.DocumentId, c.Snippet))],
        result.MissingEvidence,
        result.RequiresApproval,
        result.ActionStatus,
        result.AuditEventIds);
}

/// <summary>One audit record, scoped to the calling tenant.</summary>
public sealed record AuditEventResponse(
    string EventId,
    string Timestamp,
    AuditEventType EventType,
    string TenantId,
    string UserId,
    string Role,
    IReadOnlyDictionary<string, object?> Details)
{
    /// <summary>Project a domain audit event onto the wire contract.</summary>
    public static AuditEventResponse From(AuditEvent e) => new(
        e.EventId, e.Timestamp, e.EventType, e.TenantId, e.UserId, e.Role, e.Details);
}

/// <summary>Current approval state of a vendor, for the calling tenant only.</summary>
public sealed record VendorStatusResponse(string TenantId, string VendorId, string Status);

/// <summary>
/// A tenant-scoped evidence document.
/// </summary>
/// <remarks>
/// <see cref="Text"/> is untrusted vendor prose. It is returned so the isolation property is
/// directly observable — two tenants, one vendor, disjoint document sets — and is safe to
/// return only because it is scoped to the caller's own tenant.
/// </remarks>
public sealed record DocumentResponse(
    string DocumentId,
    string TenantId,
    string VendorId,
    string DocType,
    string Title,
    string Text,
    bool HasSoc2,
    bool HasEncryption,
    bool HasBreachNotification,
    bool HasRetentionSchedule)
{
    /// <summary>Project a domain document onto the wire contract.</summary>
    public static DocumentResponse From(Document d) => new(
        d.DocumentId, d.TenantId, d.VendorId, d.DocType, d.Title, d.Text,
        d.HasSoc2, d.HasEncryption, d.HasBreachNotification, d.HasRetentionSchedule);
}
