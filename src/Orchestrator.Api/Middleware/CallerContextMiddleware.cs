using Orchestrator.Core.Models;

namespace Orchestrator.Api.Middleware;

/// <summary>
/// Resolves the caller's identity from request headers into an <see cref="ICallerContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a stand-in for an identity provider, not authentication.</b> Anyone who can
/// reach the port can assert any identity. The headers exist to show <i>where</i> verified
/// claims attach in a real deployment — an OIDC/JWT principal validated server-side — and to
/// keep identity structurally out of the request body, which is the part that matters for
/// the design. See <c>THREAT_NOTES.md</c>.
/// </para>
/// <para>
/// The tenant is deliberately <b>not</b> validated here. Tenant filtering stays a single
/// choke point in the evidence store; duplicating it at the edge would create a second place
/// to forget it and a second place for the two to disagree.
/// </para>
/// </remarks>
public sealed class CallerContextMiddleware(RequestDelegate next)
{
    /// <summary>Paths served without a caller identity.</summary>
    private static readonly string[] AnonymousPaths = ["/health", "/openapi"];

    public async Task InvokeAsync(HttpContext context, ICallerContextAccessor accessor)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (AnonymousPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var tenantId = context.Request.Headers[CallerContext.TenantHeader].ToString();
        var userId = context.Request.Headers[CallerContext.UserHeader].ToString();
        var roleHeader = context.Request.Headers[CallerContext.RoleHeader].ToString();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidRequestException($"missing required header: {CallerContext.TenantHeader}");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidRequestException($"missing required header: {CallerContext.UserHeader}");
        }

        if (string.IsNullOrWhiteSpace(roleHeader))
        {
            throw new InvalidRequestException($"missing required header: {CallerContext.RoleHeader}");
        }

        // Fail closed on an unrecognised role rather than defaulting to the least-privileged
        // one: a typo should be a loud 400, not a silent demotion the caller never notices.
        if (!Enum.TryParse<Role>(roleHeader, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            throw new InvalidRequestException(
                $"unknown role: '{roleHeader}'. Valid roles are viewer, analyst, approver.");
        }

        accessor.Current = new CallerContext
        {
            TenantId = tenantId,
            UserId = userId,
            Role = role,
        };

        await next(context);
    }
}

/// <summary>Per-request holder for the resolved caller.</summary>
public interface ICallerContextAccessor
{
    /// <summary>The caller, once the middleware has resolved it.</summary>
    ICallerContext? Current { get; set; }
}

/// <inheritdoc cref="ICallerContextAccessor"/>
public sealed class CallerContextAccessor : ICallerContextAccessor
{
    /// <inheritdoc/>
    public ICallerContext? Current { get; set; }
}
