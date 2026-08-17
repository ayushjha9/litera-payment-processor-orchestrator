using Orchestrator.Core.Models;

namespace Orchestrator.Core.Workflow;

/// <summary>
/// A fully-resolved workflow invocation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TenantId"/>, <see cref="UserId"/> and <see cref="Role"/> are the <i>caller's
/// identity</i>. In the Python original they arrived as request-body fields; over HTTP that
/// would let any caller assert <c>role=approver</c>, so the API resolves them from the
/// authenticated principal and the request body cannot carry them at all.
/// </para>
/// <para>
/// <see cref="ApprovedBy"/> is deliberately different: it is not the caller's identity but an
/// artefact the caller presents <i>about a third party</i>, so it does belong in the body.
/// It is verified against the tenant's approver registry, and against
/// <see cref="UserId"/> to refuse self-approval.
/// </para>
/// </remarks>
public sealed record WorkflowRequest
{
    public required string TenantId { get; init; }

    public required string UserId { get; init; }

    public required Role Role { get; init; }

    public required string Question { get; init; }

    /// <summary>Optional. Must be on the action allow-list to be executable.</summary>
    public string? RequestedAction { get; init; }

    /// <summary>Optional. The claimed approver — verified, never trusted.</summary>
    public string? ApprovedBy { get; init; }
}
