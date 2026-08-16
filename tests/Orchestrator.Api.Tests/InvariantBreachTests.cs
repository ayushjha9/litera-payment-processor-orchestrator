using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Models;

namespace Orchestrator.Api.Tests;

/// <summary>
/// An evidence store whose citation allow-list disagrees with what it returns.
/// </summary>
/// <remarks>
/// Simulates a leak introduced elsewhere in the system: documents come back normally, but
/// the tenant's allowed-id set is empty, so every citation is out of bounds. This is exactly
/// the regression the output-side validation exists to catch.
/// </remarks>
file sealed class LeakyEvidenceStore : IEvidenceStore
{
    private readonly InMemoryEvidenceStore _inner = new();

    public IReadOnlyList<Document> Search(string tenantId, string vendorId, string? question = null) =>
        _inner.Search(tenantId, vendorId, question);

    public IReadOnlySet<string> DocumentIdsForTenant(string tenantId) => new HashSet<string>();
}

/// <summary>
/// A violated safety invariant must fail the request loudly on the server and say nothing
/// useful to the client.
/// </summary>
public sealed class InvariantBreachTests
{
    [Fact]
    public async Task Output_contract_violation_is_an_opaque_500()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEvidenceStore>();
                services.AddSingleton<IEvidenceStore, LeakyEvidenceStore>();
            }));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant-b");
        client.DefaultRequestHeaders.Add("X-User-Id", "approver@tenant-b.example");
        client.DefaultRequestHeaders.Add("X-Role", "approver");

        var response = await ApiFactory.Run(client);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // The client learns nothing about which control failed or which documents exist.
        Assert.DoesNotContain("citation", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contract-b-002", body);
        Assert.DoesNotContain("policy-b-001", body);
    }
}
