using System.Net;
using System.Net.Http.Json;

namespace Orchestrator.Api.Tests;

/// <summary>
/// The boundary the HTTP edge introduces: identity is asserted by the caller, so it must not
/// be readable from anything the caller can put in a body.
/// </summary>
public sealed class IdentityBoundaryTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public IdentityBoundaryTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Role_supplied_in_the_body_cannot_escalate_privilege()
    {
        using var client = _factory.ClientFor("tenant-b", "viewer@tenant-b.example", "viewer");

        var response = await ApiFactory.PostRaw(client, $$"""
            {"question": "{{ApiFactory.Question}}", "role": "approver",
             "requestedAction": "markVendorApproved", "approvedBy": "compliance@tenant-b.example"}
            """);

        // Rejected outright rather than silently ignored: a caller who thinks they changed
        // the decision finds out that they did not.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_supplied_in_the_body_cannot_redirect_the_query()
    {
        using var client = _factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        var response = await ApiFactory.PostRaw(client,
            $$"""{"question": "{{ApiFactory.Question}}", "tenantId": "tenant-b"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_body_field_is_rejected()
    {
        using var client = _factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        var response = await ApiFactory.PostRaw(client,
            $$"""{"question": "{{ApiFactory.Question}}", "forceApprove": true}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "u@tenant-a.example", "analyst")]
    [InlineData("tenant-a", null, "analyst")]
    [InlineData("tenant-a", "u@tenant-a.example", null)]
    [InlineData("", "u@tenant-a.example", "analyst")]
    public async Task Missing_identity_headers_are_refused(string? tenant, string? user, string? role)
    {
        using var client = _factory.CreateClient();
        if (tenant is not null) client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        if (user is not null) client.DefaultRequestHeaders.Add("X-User-Id", user);
        if (role is not null) client.DefaultRequestHeaders.Add("X-Role", role);

        var response = await ApiFactory.Run(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_role_header_is_refused()
    {
        using var client = _factory.ClientFor("tenant-a", "x@tenant-a.example", "superadmin");

        var response = await ApiFactory.Run(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_question_is_refused()
    {
        using var client = _factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        var response = await ApiFactory.PostRaw(client, """{"requestedAction": "markVendorApproved"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_tenant_fails_closed_without_confirming_which_tenants_exist()
    {
        using var client = _factory.ClientFor("tenant-zzz", "attacker@example.com", "approver");

        var response = await ApiFactory.Run(client);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Echoing the rejected tenant back would make this a tenant-enumeration oracle.
        Assert.DoesNotContain("tenant-zzz", body);
    }
}
