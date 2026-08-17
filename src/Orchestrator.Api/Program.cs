using Orchestrator.Api.Endpoints;
using Orchestrator.Api.ErrorHandling;
using Orchestrator.Api.Middleware;
using Orchestrator.Api.Serialization;
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

builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

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

app.MapApiEndpoints();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the app in tests.
/// </summary>
public partial class Program;
