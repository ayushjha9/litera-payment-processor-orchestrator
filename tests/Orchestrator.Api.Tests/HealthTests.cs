using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orchestrator.Core.Audit;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Models;

namespace Orchestrator.Api.Tests;

/// <summary>
/// Liveness and readiness answer independently, and readiness fails with a status code rather
/// than a cheerful body.
/// </summary>
public sealed class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health")] // retained alias
    public async Task Liveness_is_200_and_needs_no_identity(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_is_200_when_dependencies_resolve()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", body.GetProperty("status").GetString());
        Assert.Equal("healthy", body.GetProperty("checks").GetProperty("dependencies").GetString());
    }

    [Fact]
    public async Task Readiness_is_503_with_problem_details_when_a_dependency_fails()
    {
        using var broken = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEvidenceStore>();
                services.AddSingleton<IEvidenceStore, FailingEvidenceStore>();
            }));

        var response = await broken.CreateClient().GetAsync("/health/ready");

        // The point of the requirement: a broken dependency is a status code, not a 200 whose
        // body politely mentions the problem. An orchestrator reads the code and nothing else.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(503, body.GetProperty("status").GetInt32());
        Assert.Equal("unhealthy", body.GetProperty("checks").GetProperty("dependencies").GetString());
    }

    [Fact]
    public async Task Readiness_failure_does_not_leak_the_underlying_exception()
    {
        using var broken = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuditLog>();
                services.AddSingleton<IAuditLog, FailingAuditLog>();
            }));

        var response = await broken.CreateClient().GetAsync("/health/ready");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // /health/ready is unauthenticated. An exception message is internal detail and belongs
        // in the log, not in a body anyone who can reach the port may read.
        Assert.DoesNotContain(FailingAuditLog.SecretDetail, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Readiness_probe_does_not_write_to_the_audit_log()
    {
        var client = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        var before = await client.GetFromJsonAsync<List<AuditDto>>("/api/v1/audit", ApiFactory.Json);
        for (var i = 0; i < 5; i++)
        {
            await factory.CreateClient().GetAsync("/health/ready");
        }

        var after = await client.GetFromJsonAsync<List<AuditDto>>("/api/v1/audit", ApiFactory.Json);

        // A probe runs every few seconds forever. One that appended would drown the compliance
        // record it is supposed to be protecting.
        Assert.Equal(before!.Count, after!.Count);
    }
}

/// <summary>An evidence store that is resolvable but not answering.</summary>
file sealed class FailingEvidenceStore : IEvidenceStore
{
    public IReadOnlyList<Document> Search(string tenantId, string vendorId, string? question = null) =>
        throw new InvalidOperationException("evidence store unavailable");

    public IReadOnlySet<string> DocumentIdsForTenant(string tenantId) =>
        throw new InvalidOperationException("evidence store unavailable");
}

/// <summary>An audit log whose read path throws a distinctive message.</summary>
file sealed class FailingAuditLog : IAuditLog
{
    public const string SecretDetail = "connection string pw=hunter2 at AuditLog.Read";

    public string Write(
        AuditEventType eventType,
        string tenantId,
        string userId,
        string role,
        IReadOnlyDictionary<string, object?>? details = null) => "evt-000000";

    public IReadOnlyList<AuditEvent> Read(string? tenantId = null) =>
        throw new InvalidOperationException(SecretDetail);
}
