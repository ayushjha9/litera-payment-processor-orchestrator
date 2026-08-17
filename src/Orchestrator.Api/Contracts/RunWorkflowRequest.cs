using System.Text.Json.Serialization;

namespace Orchestrator.Api.Contracts;

/// <summary>
/// The body of <c>POST /api/v1/workflow/run</c>.
/// </summary>
/// <remarks>
/// <para>
/// Notice what is <b>absent</b>: <c>tenantId</c>, <c>userId</c> and <c>role</c>. Those are
/// the caller's identity and are resolved from the authenticated principal (here, the
/// <c>X-Tenant-Id</c> / <c>X-User-Id</c> / <c>X-Role</c> headers). Accepting them here would
/// let any caller assert <c>role: "approver"</c> and walk through the authorization boundary.
/// </para>
/// <para>
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> makes that refusal explicit rather than
/// silent: sending <c>role</c> — or any other unrecognised field, such as
/// <c>forceApprove</c> — is a <c>400</c>, not a quietly ignored property. Unknown fields are
/// rejected at the boundary rather than dropped, so a caller who believes they are changing
/// the decision finds out that they are not.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RunWorkflowRequest
{
    /// <summary>The question being asked of the evidence. Required.</summary>
    public string? Question { get; init; }

    /// <summary>Optional action to attempt, e.g. <c>markVendorApproved</c>.</summary>
    public string? RequestedAction { get; init; }

    /// <summary>
    /// Optional claimed approver. Not the caller's own identity — an artefact presented about
    /// a third party, verified against the tenant's approver registry and refused if it
    /// matches the caller (no self-approval).
    /// </summary>
    public string? ApprovedBy { get; init; }
}
