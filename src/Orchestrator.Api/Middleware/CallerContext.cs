using Orchestrator.Core.Models;

namespace Orchestrator.Api.Middleware;

/// <summary>
/// The authenticated caller.
/// </summary>
/// <remarks>
/// Populated by <see cref="CallerContextMiddleware"/> from request headers and resolved from
/// DI by the endpoints. Endpoints read identity from here and never from a request body.
/// </remarks>
public interface ICallerContext
{
    string TenantId { get; }

    string UserId { get; }

    Role Role { get; }
}

/// <inheritdoc cref="ICallerContext"/>
public sealed class CallerContext : ICallerContext
{
    /// <summary>Header carrying the caller's tenant.</summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>Header carrying the caller's user id.</summary>
    public const string UserHeader = "X-User-Id";

    /// <summary>Header carrying the caller's role.</summary>
    public const string RoleHeader = "X-Role";

    public required string TenantId { get; init; }

    public required string UserId { get; init; }

    public required Role Role { get; init; }
}
