using Orchestrator.Ui.Api;
using Orchestrator.Ui.Components;
using Orchestrator.Ui.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Per-circuit: one asserted identity for the life of a user's connection, shared by every page
// they navigate to. Held server-side — the browser never sees it. See SessionIdentity.
builder.Services.AddScoped<ISessionIdentity, SessionIdentity>();
builder.Services.AddScoped<CallerHeaderHandler>();

var apiBaseUrl = builder.Configuration["Orchestrator:ApiBaseUrl"] ?? "http://localhost:5180";

// The only component that talks to the API. Every call goes through CallerHeaderHandler, so no
// page can send an identity other than the session's, or forget to send one.
builder.Services.AddHttpClient<OrchestratorApiClient>(client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddHttpMessageHandler<CallerHeaderHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed so the UI can be hosted in tests.</summary>
public partial class Program;
