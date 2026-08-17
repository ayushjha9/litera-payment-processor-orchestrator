namespace Orchestrator.Core.Models;

/// <summary>
/// Raised when a workflow is invoked for a tenant we do not know about.
/// </summary>
/// <remarks>
/// Fail closed: an unrecognised tenant must never fall through to an unfiltered document
/// set. At the API edge this maps to <c>403</c> — deliberately without echoing the
/// offending tenant id, which would turn failing closed into a tenant-enumeration oracle.
/// </remarks>
public sealed class UnknownTenantException(string tenantId)
    : Exception($"unknown tenant: '{tenantId}'")
{
    /// <summary>The tenant that was not recognised. For logs only — never for a response body.</summary>
    public string TenantId { get; } = tenantId;
}

/// <summary>
/// Raised when a <see cref="WorkflowResult"/> violates the output contract or a safety invariant.
/// </summary>
/// <remarks>
/// These are should-never-happen conditions. They mean a control failed, so they surface as
/// an opaque <c>500</c> and deserve an alert rather than a log line.
/// </remarks>
public sealed class OutputContractException(string message) : Exception(message);

/// <summary>
/// Raised when an inbound request is malformed: a missing required field, an unknown role,
/// or an unrecognised field. Maps to <c>400</c>.
/// </summary>
public sealed class InvalidRequestException(string message) : Exception(message);
