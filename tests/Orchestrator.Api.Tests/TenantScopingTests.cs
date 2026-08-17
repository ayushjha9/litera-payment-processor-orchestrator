using System.Net.Http.Json;
using Orchestrator.Core.Fixtures;

namespace Orchestrator.Api.Tests;

/// <summary>
/// Isolation as observed from outside: the same routes, two tenants, disjoint everything.
/// </summary>
public sealed class TenantScopingTests
{
    [Fact]
    public async Task Audit_endpoint_returns_only_the_calling_tenants_events()
    {
        using var factory = new ApiFactory();
        using var b = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");
        using var a = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        await ApiFactory.Run(b, requestedAction: "markVendorApproved");
        await ApiFactory.Run(a);

        var events = await a.GetFromJsonAsync<List<AuditDto>>("/api/v1/audit", ApiFactory.Json);

        Assert.NotEmpty(events!);
        Assert.All(events!, e => Assert.Equal("tenant-a", e.TenantId));
    }

    [Fact]
    public async Task Audit_endpoint_never_carries_untrusted_document_text()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");

        await ApiFactory.Run(client, requestedAction: "markVendorApproved");
        var raw = await (await client.GetAsync("/api/v1/audit")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(EvidenceFixtures.InjectedText, raw);
        Assert.Contains("contract-b-002", raw); // ids, not text
    }

    [Fact]
    public async Task Evidence_endpoint_returns_disjoint_document_sets()
    {
        using var factory = new ApiFactory();
        using var a = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");
        using var b = factory.ClientFor("tenant-b", "analyst@tenant-b.example", "analyst");

        var docsA = await a.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/v1/evidence", ApiFactory.Json);
        var docsB = await b.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/v1/evidence", ApiFactory.Json);

        var idsA = docsA!.Select(d => d["documentId"].ToString()).ToHashSet();
        var idsB = docsB!.Select(d => d["documentId"].ToString()).ToHashSet();

        Assert.Empty(idsA.Intersect(idsB));
    }

    [Fact]
    public async Task Vendor_status_is_per_tenant()
    {
        using var factory = new ApiFactory();
        using var b = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");
        using var a = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        await ApiFactory.Run(b, requestedAction: "markVendorApproved", approvedBy: "compliance@tenant-b.example");

        var statusB = await b.GetFromJsonAsync<Dictionary<string, string>>(
            "/api/v1/vendors/vendor-x/status", ApiFactory.Json);
        var statusA = await a.GetFromJsonAsync<Dictionary<string, string>>(
            "/api/v1/vendors/vendor-x/status", ApiFactory.Json);

        // Approving Vendor X for one tenant says nothing about another.
        Assert.Equal("approved", statusB!["status"]);
        Assert.Equal("pending", statusA!["status"]);
    }
}

/// <summary>
/// The audit log and vendor state are singletons shared across concurrent requests — a
/// property the single-threaded Python original never had to hold.
/// </summary>
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Concurrent_runs_do_not_mix_audit_ids_between_responses()
    {
        using var factory = new ApiFactory();
        using var a = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");
        using var b = factory.ClientFor("tenant-b", "analyst@tenant-b.example", "analyst");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 25).Select(i => ApiFactory.Run(i % 2 == 0 ? a : b)));

        var idLists = new List<List<string>>();
        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            var dto = (await response.Content.ReadFromJsonAsync<WorkflowDto>(ApiFactory.Json))!;
            // An advisory run writes exactly two events: workflow_run and decision.
            Assert.Equal(2, dto.AuditEventIds.Count);
            idLists.Add(dto.AuditEventIds);
        }

        // No id may be claimed by two different responses.
        var all = idLists.SelectMany(x => x).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public async Task Audit_ids_are_gap_free_and_monotonic_under_load()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => ApiFactory.Run(client)));

        var events = await client.GetFromJsonAsync<List<AuditDto>>("/api/v1/audit", ApiFactory.Json);
        var numbers = events!.Select(e => int.Parse(e.EventId.Split('-')[1])).ToList();

        Assert.Equal(50, numbers.Count); // 25 runs x 2 events
        Assert.Equal(Enumerable.Range(1, 50), numbers.Order());
    }
}
