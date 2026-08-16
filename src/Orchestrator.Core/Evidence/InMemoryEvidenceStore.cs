using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Evidence;

/// <inheritdoc cref="IEvidenceStore"/>
public sealed class InMemoryEvidenceStore : IEvidenceStore
{
    /// <inheritdoc/>
    public IReadOnlyList<Document> Search(string tenantId, string vendorId, string? question = null)
    {
        // Fail closed. An unrecognised tenant must never fall through to an unfiltered set.
        if (!EvidenceFixtures.Tenants.ContainsKey(tenantId))
        {
            throw new UnknownTenantException(tenantId);
        }

        return EvidenceFixtures.All()
            .Where(d => d.TenantId == tenantId && d.VendorId == vendorId)
            .ToList();
    }

    /// <inheritdoc/>
    public IReadOnlySet<string> DocumentIdsForTenant(string tenantId) =>
        EvidenceFixtures.DocumentIdsForTenant(tenantId);
}
