using Bunit;
using Orchestrator.Ui.Api;
using Orchestrator.Ui.Components.Contracts;
using Orchestrator.Ui.Components.Identity;
using Orchestrator.Ui.Identity;

namespace Orchestrator.Ui.Tests;

/// <summary>
/// Identity stays on the server and out of the request body.
/// </summary>
/// <remarks>
/// The API deliberately removed identity from the request body so a caller cannot assert its
/// own role. A UI with a role dropdown puts that control back in the most inviting place
/// possible, so these tests pin where it actually lives.
/// </remarks>
public sealed class IdentityTests : BunitContext
{
    [Fact]
    public void The_picker_raises_the_full_identity_when_the_role_changes()
    {
        (string TenantId, string UserId, RoleDto Role)? captured = null;

        var component = Render<IdentityPicker>(p => p
            .Add(c => c.TenantId, "tenant-b")
            .Add(c => c.UserId, "approver@tenant-b.example")
            .Add(c => c.Role, RoleDto.Analyst)
            .Add(c => c.Tenants, SessionIdentity.KnownTenants)
            .Add(c => c.IdentityChanged, identity => captured = identity));

        component.FindAll("select")[1].Change(nameof(RoleDto.Approver));

        Assert.NotNull(captured);
        Assert.Equal("tenant-b", captured.Value.TenantId);
        Assert.Equal(RoleDto.Approver, captured.Value.Role);
    }

    [Fact]
    public void The_picker_never_names_a_header_in_its_markup()
    {
        var component = Render<IdentityPicker>(p => p
            .Add(c => c.Tenants, SessionIdentity.KnownTenants));

        // The component is a plain input control. It knows nothing about HTTP, and the header
        // names appear only in CallerHeaderHandler on the server — one place, not two.
        foreach (var header in new[] { "X-Tenant-Id", "X-User-Id", "X-Role" })
        {
            Assert.DoesNotContain(header, component.Markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_picker_states_on_screen_that_it_is_not_authentication()
    {
        var component = Render<IdentityPicker>(p => p
            .Add(c => c.Tenants, SessionIdentity.KnownTenants));

        // A tenant/user/role selector reads as a login. The warning is part of the component
        // rather than something a host page could forget to add.
        var warning = component.Find(".oc-identity__warning").TextContent;
        Assert.Contains("Not authentication", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_handler_attaches_all_three_identity_headers()
    {
        var identity = new SessionIdentity();
        identity.Set("tenant-b", "approver@tenant-b.example", RoleDto.Approver);

        var (request, _) = await SendAsync(identity, new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit"));

        Assert.Equal("tenant-b", request.Headers.GetValues(CallerHeaderHandler.TenantHeader).Single());
        Assert.Equal(
            "approver@tenant-b.example", request.Headers.GetValues(CallerHeaderHandler.UserHeader).Single());

        // The wire spelling the API parses, not the C# enum name.
        Assert.Equal("approver", request.Headers.GetValues(CallerHeaderHandler.RoleHeader).Single());
    }

    [Fact]
    public async Task The_handler_does_not_accumulate_headers_when_a_request_is_retried()
    {
        var identity = new SessionIdentity();
        var message = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit");

        // The same message sent twice, as a retrying handler would. Adding rather than
        // replacing would leave the API reading a joined "tenant-a, tenant-a".
        await SendAsync(identity, message);
        var (request, _) = await SendAsync(identity, message);

        Assert.Single(request.Headers.GetValues(CallerHeaderHandler.TenantHeader));
    }

    [Fact]
    public async Task No_identity_is_sent_in_the_request_body()
    {
        var identity = new SessionIdentity();
        identity.Set("tenant-b", "approver@tenant-b.example", RoleDto.Approver);

        var body = System.Text.Json.JsonSerializer.Serialize(
            new RunWorkflowRequestDto("Can we approve Vendor X?", "markVendorApproved", null),
            WireFormat.Json);

        // The API rejects unknown body fields, so a client that put identity here would get a
        // 400. Asserting the shape here catches it at the client instead.
        foreach (var field in new[] { "tenantId", "userId", "role" })
        {
            Assert.DoesNotContain(field, body, StringComparison.OrdinalIgnoreCase);
        }

        var (request, _) = await SendAsync(
            identity, new HttpRequestMessage(HttpMethod.Post, "/api/v1/workflow/run")
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });

        Assert.DoesNotContain("approver", await request.Content!.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_the_session_identity_notifies_subscribers()
    {
        var identity = new SessionIdentity();
        var notified = 0;
        identity.Changed += () => notified++;

        identity.Set("tenant-b", "someone@tenant-b.example", RoleDto.Viewer);

        // Open pages re-fetch on this, so one tenant's data is never left on screen under
        // another tenant's identity.
        Assert.Equal(1, notified);
        Assert.Equal("tenant-b", identity.TenantId);
    }

    [Fact]
    public void The_session_trims_but_does_not_validate()
    {
        var identity = new SessionIdentity();
        identity.Set("  tenant-zzz  ", " nobody@example.test ", RoleDto.Approver);

        // The API is the authority on what a valid tenant is and fails closed on it. Filtering
        // here would put a second, weaker copy of that judgement in the UI and hide the API's
        // own 403 from a reviewer who should be able to watch it happen.
        Assert.Equal("tenant-zzz", identity.TenantId);
        Assert.Equal("nobody@example.test", identity.UserId);
    }

    private static async Task<(HttpRequestMessage Request, HttpResponseMessage Response)> SendAsync(
        ISessionIdentity identity, HttpRequestMessage request)
    {
        var capture = new CapturingHandler();
        var handler = new CallerHeaderHandler(identity) { InnerHandler = capture };
        var response = await new HttpMessageInvoker(handler).SendAsync(request, CancellationToken.None);
        return (capture.Request!, response);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
