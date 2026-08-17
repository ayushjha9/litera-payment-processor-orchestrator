using Orchestrator.Core.Models;

namespace Orchestrator.Core.Evidence;

/// <summary>Tenant-scoped evidence retrieval — the single tenant-filtering choke point.</summary>
public interface IEvidenceStore
{
    /// <summary>
    /// Return the documents this tenant holds for this vendor.
    /// </summary>
    /// <remarks>
    /// Tenant filtering happens here and nowhere else, so it can be reasoned about and
    /// tested in one place. There is no caller-supplied document list and no fallback path:
    /// an unknown tenant throws rather than returning anything.
    /// </remarks>
    /// <param name="tenantId">The requesting tenant. The only place this filter is applied.</param>
    /// <param name="vendorId">The vendor under assessment.</param>
    /// <param name="question">
    /// Accepted for signature fidelity and audit context; retrieval in this mock is
    /// exhaustive for the vendor rather than ranked, since a real retriever
    /// (BM25/embeddings) is out of scope.
    /// </param>
    /// <exception cref="UnknownTenantException">If the tenant is not recognised.</exception>
    IReadOnlyList<Document> Search(string tenantId, string vendorId, string? question = null);

    /// <summary>The requesting tenant's document ids, for output-side citation validation.</summary>
    IReadOnlySet<string> DocumentIdsForTenant(string tenantId);
}
