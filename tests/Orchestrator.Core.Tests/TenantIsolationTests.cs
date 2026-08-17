using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Tests;

/// <summary>
/// Two tenants hold evidence for the same vendor. Neither may ever see the other's.
/// </summary>
public sealed class TenantIsolationTests
{
    private readonly WorkflowFixture _fixture = new();

    [Fact]
    public void Search_returns_only_the_requesting_tenants_documents()
    {
        var ids = _fixture.EvidenceStore
            .Search("tenant-a", EvidenceFixtures.VendorId, WorkflowFixture.Question)
            .Select(d => d.DocumentId)
            .ToHashSet();

        Assert.Equal(["contract-a-002", "policy-a-001"], ids.Order());
        Assert.Empty(ids.Intersect(EvidenceFixtures.DocumentIdsForTenant("tenant-b")));
    }

    [Fact]
    public void Each_tenant_sees_its_own_evidence_for_the_same_vendor()
    {
        var a = _fixture.EvidenceStore.Search("tenant-a", EvidenceFixtures.VendorId)
            .Select(d => d.DocumentId).ToHashSet();
        var b = _fixture.EvidenceStore.Search("tenant-b", EvidenceFixtures.VendorId)
            .Select(d => d.DocumentId).ToHashSet();

        Assert.Empty(a.Intersect(b));
        Assert.Equal(["contract-b-002", "policy-b-001"], b.Order());
    }

    [Fact]
    public void Workflow_never_cites_another_tenants_document()
    {
        foreach (var tenant in (string[])["tenant-a", "tenant-b"])
        {
            var result = _fixture.Run(tenant, $"analyst@{tenant}.example", Role.Analyst);
            var cited = result.Citations.Select(c => c.DocumentId).ToHashSet();

            Assert.NotEmpty(cited);
            Assert.Subset(EvidenceFixtures.DocumentIdsForTenant(tenant).ToHashSet(), cited);
        }
    }

    [Fact]
    public void Same_question_yields_different_answers_per_tenant()
    {
        var a = _fixture.Run("tenant-a", "analyst@tenant-a.example", Role.Analyst);
        var b = _fixture.Run("tenant-b", "analyst@tenant-b.example", Role.Analyst);

        Assert.Equal(RiskLevel.Medium, a.RiskLevel);
        Assert.Equal(RiskLevel.High, b.RiskLevel);
    }

    [Fact]
    public void Unknown_tenant_fails_closed()
    {
        Assert.Throws<UnknownTenantException>(
            () => _fixture.EvidenceStore.Search("tenant-zzz", EvidenceFixtures.VendorId));

        Assert.Throws<UnknownTenantException>(
            () => _fixture.Run("tenant-zzz", "attacker@example.com", Role.Approver));
    }
}
