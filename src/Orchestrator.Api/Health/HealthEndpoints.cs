using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orchestrator.Api.Health;

/// <summary>Liveness and readiness probes.</summary>
/// <remarks>
/// The two are answered separately because an orchestrator does different things with them:
/// a failing liveness probe gets the process restarted, a failing readiness probe gets it taken
/// out of the load-balancer pool. Collapsing them into one endpoint means a dependency blip
/// restarts a perfectly healthy process.
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>Route prefixes served without a caller identity.</summary>
    /// <remarks>
    /// A probe is issued by an orchestrator that has no tenant, and a Prometheus scraper cannot
    /// present identity headers either — so these paths bypass
    /// <c>CallerContextMiddleware</c>. Everything they return must therefore be safe to show an
    /// unauthenticated client, which is why the readiness body carries check names and statuses
    /// and never an exception message. <c>/metrics</c> is the sharper edge of this: it exposes
    /// per-tenant request volumes and should be restricted at the network, not the application.
    /// See <c>PRODUCTION_NOTES.md</c>.
    /// </remarks>
    public static readonly string[] AnonymousPaths = ["/health", "/metrics", "/openapi"];

    /// <summary>Map <c>/health/live</c>, <c>/health/ready</c>, and the legacy <c>/health</c>.</summary>
    public static void MapHealthEndpoints(this WebApplication app)
    {
        // Liveness: the process is up and serving. No checks run — a liveness probe that
        // consulted dependencies would restart the process over a dependency outage, which
        // fixes nothing and drops in-flight requests.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteLive,
        })
            .WithName("HealthLive")
            .WithSummary("Liveness probe. 200 whenever the process is serving.");

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReady,

            // Degraded maps to 503 as well, overriding the framework default of 200. A
            // half-working instance that answers 200 keeps receiving traffic it cannot serve;
            // "ready" is a binary question and this endpoint answers it as one.
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        })
            .WithName("HealthReady")
            .WithSummary("Readiness probe. 503 when a dependency is not answering.");

        // Retained so existing probe configuration keeps working; /health/live is the one to
        // point new configuration at.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteLive,
        })
            .WithName("Health")
            .WithSummary("Deprecated alias for /health/live.");
    }

    /// <summary>Tag marking a check as part of readiness rather than liveness.</summary>
    public const string ReadyTag = "ready";

    private static Task WriteLive(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new { status = "healthy" });
    }

    /// <summary>
    /// Healthy readiness answers with the per-check statuses; unhealthy answers with
    /// ProblemDetails.
    /// </summary>
    /// <remarks>
    /// A degraded dependency must not be reported as <c>200</c> with a body saying so. Probes
    /// are consumed by orchestrators that read the status code and nothing else, so a body-only
    /// signal is a signal nobody receives — the instance would stay in the pool taking traffic
    /// it cannot serve. The status code is the contract.
    /// </remarks>
    private static Task WriteReady(HttpContext context, HealthReport report)
    {
        // Names and statuses only. This endpoint is unauthenticated; exception detail stays in
        // the log, where the failure is already recorded in full.
        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => JsonNamingPolicy.SnakeCaseLower.ConvertName(entry.Value.Status.ToString()));

        if (report.Status == HealthStatus.Healthy)
        {
            return context.Response.WriteAsJsonAsync(new { status = "ready", checks });
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Service unavailable",
            Detail = "One or more dependencies are not ready to serve requests.",
            Instance = context.Request.Path,
        };
        problem.Extensions["checks"] = checks;

        // The content type is passed here rather than assigned to Response.ContentType, which
        // WriteAsJsonAsync would overwrite with application/json.
        return context.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json");
    }
}
