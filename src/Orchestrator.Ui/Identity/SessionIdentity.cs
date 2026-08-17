using Orchestrator.Ui.Components.Contracts;

namespace Orchestrator.Ui.Identity;

/// <summary>
/// The identity this UI session asserts to the API.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped, which under the Interactive Server render mode means <i>per circuit</i> —
/// it lives as long as the user's connection and is shared by every page they navigate to.
/// </para>
/// <para>
/// It lives on the <b>server</b>. That is the whole point: the browser exchanges only SignalR
/// messages with this app and never sees, sets or can edit the identity headers that reach the
/// API. This mirrors <c>CallerContextMiddleware</c> on the API side — identity resolved once,
/// in one place, never taken from anything the far side controls.
/// </para>
/// <para>
/// It is still not authentication. It stops a browser tampering with the headers; it does
/// nothing about a network attacker, who can call the API directly. See
/// <c>THREAT_NOTES.md</c> risks 4 and 5.
/// </para>
/// </remarks>
public interface ISessionIdentity
{
    /// <summary>The tenant being asserted.</summary>
    string TenantId { get; }

    /// <summary>The user being asserted.</summary>
    string UserId { get; }

    /// <summary>The role being asserted.</summary>
    RoleDto Role { get; }

    /// <summary>Raised after any change, so open pages can refresh what they are showing.</summary>
    event Action? Changed;

    /// <summary>Replace the asserted identity.</summary>
    void Set(string tenantId, string userId, RoleDto role);
}

/// <inheritdoc cref="ISessionIdentity"/>
public sealed class SessionIdentity : ISessionIdentity
{
    /// <summary>
    /// Tenants offered in the picker.
    /// </summary>
    /// <remarks>
    /// Hard-coded here rather than fetched, because the API has no endpoint that lists
    /// tenants — deliberately. Enumerating tenants is precisely what the unknown-tenant
    /// <c>403</c> declines to enable, and adding a route for the UI's convenience would undo
    /// that. A real deployment gets the tenant from the signed token, and this list disappears
    /// along with the picker.
    /// </remarks>
    public static readonly IReadOnlyList<string> KnownTenants = ["tenant-a", "tenant-b"];

    /// <inheritdoc/>
    public string TenantId { get; private set; } = "tenant-a";

    /// <inheritdoc/>
    public string UserId { get; private set; } = "analyst@tenant-a.example";

    /// <inheritdoc/>
    public RoleDto Role { get; private set; } = RoleDto.Analyst;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <inheritdoc/>
    public void Set(string tenantId, string userId, RoleDto role)
    {
        // Trimmed but otherwise unvalidated: the API is the authority on what a valid tenant or
        // role is, and it fails closed on both. Pre-filtering here would put a second, weaker
        // copy of that judgement in the UI and hide the API's own refusals — including the 403
        // that a reviewer should be able to see happen.
        TenantId = tenantId.Trim();
        UserId = userId.Trim();
        Role = role;

        Changed?.Invoke();
    }
}
