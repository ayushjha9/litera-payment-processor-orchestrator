using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;
using Orchestrator.Api.Endpoints;
using Orchestrator.Api.ErrorHandling;
using Orchestrator.Api.Health;
using Orchestrator.Api.Middleware;
using Orchestrator.Api.Serialization;
using Orchestrator.Api.Telemetry;
using Orchestrator.Core.Actions;
using Orchestrator.Core.Approval;
using Orchestrator.Core.Audit;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Risk;
using Orchestrator.Core.Workflow;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => JsonConfig.Apply(options.SerializerOptions));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// Readiness. Liveness registers no check at all — see HealthEndpoints for why the two
// probes must not be answered by the same logic.
builder.Services.AddHealthChecks()
    .AddCheck<DependencyHealthCheck>(DependencyHealthCheck.Name, tags: [HealthEndpoints.ReadyTag]);

// Metrics. The meter comes from IMeterFactory, so it belongs to this container rather than
// the process — which is what lets parallel tests observe only their own host.
builder.Services.AddMetrics();
builder.Services.AddSingleton<WorkflowMetrics>();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(WorkflowMetrics.MeterName)
        .AddPrometheusExporter());

// Stateless domain services.
builder.Services.AddSingleton<IEvidenceStore, InMemoryEvidenceStore>();
builder.Services.AddSingleton<IRiskEvaluator, RiskEvaluator>();
builder.Services.AddSingleton<IApprovalService, ApprovalService>();
builder.Services.AddSingleton<IActionExecutor, MockActionExecutor>();

// Shared mutable state. Singletons by necessity — the audit trail and vendor state must
// outlive a request — and therefore concurrent by necessity too. Both implementations are
// internally synchronised; see InMemoryAuditLog and InMemoryVendorStateStore.
builder.Services.AddSingleton<IAuditLog, InMemoryAuditLog>();
builder.Services.AddSingleton<IVendorStateStore, InMemoryVendorStateStore>();

// The engine, wrapped in its instrumentation. The decorator is registered as the
// IWorkflowEngine everything else resolves, so no caller has to remember to instrument — and
// Orchestrator.Core stays free of any telemetry dependency.
builder.Services.AddSingleton<WorkflowEngine>();
builder.Services.AddSingleton<IWorkflowEngine>(sp => new InstrumentedWorkflowEngine(
    sp.GetRequiredService<WorkflowEngine>(),
    sp.GetRequiredService<WorkflowMetrics>()));

// One caller per request.
builder.Services.AddScoped<ICallerContextAccessor, CallerContextAccessor>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Identity is resolved before any endpoint runs, so no endpoint can read it from a body.
app.UseMiddleware<CallerContextMiddleware>();

app.MapHealthEndpoints();
app.MapApiEndpoints();

// Prometheus scrape target. Anonymous by necessity — a scraper has no tenant to present — so
// it must be restricted at the network. See PRODUCTION_NOTES.md.
app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the app in tests.
/// </summary>
public partial class Program;
