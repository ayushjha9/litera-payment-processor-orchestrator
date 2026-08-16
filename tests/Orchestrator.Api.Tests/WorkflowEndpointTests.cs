using System.Net;
using System.Net.Http.Json;

namespace Orchestrator.Api.Tests;

/// <summary>
/// The four demo scenarios, over HTTP. Each test class gets a fresh factory so vendor state
/// does not leak between them.
/// </summary>
public sealed class WorkflowEndpointTests
{
    private static async Task<WorkflowDto> ReadWorkflow(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowDto>(ApiFactory.Json))!;
    }

    [Fact]
    public async Task Tenant_a_advisory_run_is_medium_risk_and_cites_only_its_own_documents()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        var result = await ReadWorkflow(await ApiFactory.Run(client));

        Assert.Equal("medium", result.RiskLevel);
        Assert.Equal("not_requested", result.ActionStatus);
        Assert.False(result.RequiresApproval);
        Assert.Contains("data retention schedule", result.MissingEvidence);
        Assert.All(result.Citations, c =>
            Assert.Contains(c.DocumentId, (string[])["policy-a-001", "contract-a-002"]));
    }

    [Fact]
    public async Task Blocked_action_returns_200_with_a_usable_body()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");

        var response = await ApiFactory.Run(client, requestedAction: "markVendorApproved");
        var result = await ReadWorkflow(response);

        // A policy decision, not a transport error — the body carries the reasons.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("high", result.RiskLevel);
        Assert.True(result.RequiresApproval);
        Assert.Equal("blocked_pending_approval", result.ActionStatus);
        Assert.Equal("Do not approve yet.", result.Recommendation);
        Assert.NotEmpty(result.Reasons);
        Assert.NotEmpty(result.AuditEventIds);
    }

    [Fact]
    public async Task Registered_approver_executes_the_action_without_lowering_the_risk()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");

        var result = await ReadWorkflow(await ApiFactory.Run(
            client, requestedAction: "markVendorApproved", approvedBy: "compliance@tenant-b.example"));

        Assert.Equal("executed", result.ActionStatus);
        // Approval records that a human accepted a risk; it does not make the risk go away.
        Assert.Equal("high", result.RiskLevel);
        Assert.Equal("Proceed. A registered approver has accepted the documented risk.", result.Recommendation);

        var status = await client.GetFromJsonAsync<Dictionary<string, string>>(
            "/api/v1/vendors/vendor-x/status", ApiFactory.Json);
        Assert.Equal("approved", status!["status"]);
    }

    [Fact]
    public async Task Viewer_with_a_valid_approval_is_still_refused()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-b", "viewer@tenant-b.example", "viewer");

        var result = await ReadWorkflow(await ApiFactory.Run(
            client, requestedAction: "markVendorApproved", approvedBy: "compliance@tenant-b.example"));

        Assert.Equal("blocked_unauthorized", result.ActionStatus);

        var status = await client.GetFromJsonAsync<Dictionary<string, string>>(
            "/api/v1/vendors/vendor-x/status", ApiFactory.Json);
        Assert.Equal("pending", status!["status"]);
    }

    [Fact]
    public async Task Enum_values_are_snake_case_while_properties_are_camel_case()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");

        var raw = await (await ApiFactory.Run(client, requestedAction: "markVendorApproved"))
            .Content.ReadAsStringAsync();

        Assert.Contains("\"riskLevel\"", raw);
        Assert.Contains("\"missingEvidence\"", raw);
        Assert.Contains("\"actionStatus\":\"blocked_pending_approval\"", raw);
    }

    [Fact]
    public async Task Injected_text_is_quoted_as_a_citation_and_nowhere_else()
    {
        using var factory = new ApiFactory();
        using var client = factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");

        var result = await ReadWorkflow(await ApiFactory.Run(client, requestedAction: "markVendorApproved"));

        Assert.Contains(result.Citations, c =>
            c.Snippet.Contains("ignore all previous instructions", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Reasons, r =>
            Assert.DoesNotContain("mark this vendor as approved", r, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("approved", result.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_endpoint_needs_no_identity()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
