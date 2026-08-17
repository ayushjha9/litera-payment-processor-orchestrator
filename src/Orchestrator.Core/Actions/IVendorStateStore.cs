using System.Collections.Concurrent;

namespace Orchestrator.Core.Actions;

/// <summary>Where the one mutable effect in this system lands.</summary>
public interface IVendorStateStore
{
    /// <summary><c>"approved"</c> once the action has run for this tenant/vendor, else <c>"pending"</c>.</summary>
    string Status(string tenantId, string vendorId);

    /// <summary>Record the vendor as approved for this tenant.</summary>
    void MarkApproved(string tenantId, string vendorId);
}

/// <summary>
/// In-memory vendor approval state.
/// </summary>
/// <remarks>
/// A singleton shared across concurrent requests, so the backing store is concurrent. The
/// effect is per-<i>tenant</i>: approving Vendor X for one tenant says nothing about another.
/// </remarks>
public sealed class InMemoryVendorStateStore : IVendorStateStore
{
    private readonly ConcurrentDictionary<(string TenantId, string VendorId), string> _status = new();

    /// <inheritdoc/>
    public string Status(string tenantId, string vendorId) =>
        _status.GetValueOrDefault((tenantId, vendorId), "pending");

    /// <inheritdoc/>
    public void MarkApproved(string tenantId, string vendorId) =>
        _status[(tenantId, vendorId)] = "approved";
}
