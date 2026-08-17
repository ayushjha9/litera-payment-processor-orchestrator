using Orchestrator.Ui.Components.Contracts;
using Orchestrator.Ui.Identity;

namespace Orchestrator.Ui.Api;

/// <summary>
/// Attaches the caller's identity headers to every outbound API call.
/// </summary>
/// <remarks>
/// <para>
/// The one place the UI turns a session identity into <c>X-Tenant-Id</c> / <c>X-User-Id</c> /
/// <c>X-Role</c>. Putting it in a <see cref="DelegatingHandler"/> rather than at each call site
/// means no page can forget, and no page can send a <i>different</i> identity than the session
/// holds — the same reason the API resolves identity in middleware instead of per endpoint.
/// </para>
/// <para>
/// Nothing identity-shaped goes in a request body. That is not incidental: the API rejects
/// unknown body fields precisely so a caller cannot smuggle a role past the boundary, and a
/// client that tried would get a <c>400</c>. Pinned by <c>CallerHeaderHandlerTests</c>.
/// </para>
/// </remarks>
public sealed class CallerHeaderHandler(ISessionIdentity identity) : DelegatingHandler
{
    /// <summary>The header names, matching <c>Orchestrator.Api.Middleware.CallerContext</c>.</summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <inheritdoc cref="TenantHeader"/>
    public const string UserHeader = "X-User-Id";

    /// <inheritdoc cref="TenantHeader"/>
    public const string RoleHeader = "X-Role";

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Remove-then-add rather than add: a retried request would otherwise accumulate a
        // second value per header, and the API reads the joined string.
        Set(request, TenantHeader, identity.TenantId);
        Set(request, UserHeader, identity.UserId);
        Set(request, RoleHeader, identity.Role.Wire());

        return base.SendAsync(request, cancellationToken);
    }

    private static void Set(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);

        // A blank value is still sent, so the API's own "missing required header" 400 is what
        // the user sees. Substituting a default here would hide a real refusal behind a
        // silently-corrected request.
        request.Headers.TryAddWithoutValidation(name, value);
    }
}
