using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orchestrator.Core.Audit;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Fixtures;

namespace Orchestrator.Api.Health;

/// <summary>
/// Readiness: are the evidence store and audit log resolvable, and do they answer?
/// </summary>
/// <remarks>
/// <para>
/// Both dependencies are constructor-injected, so DI resolution is proven by this class
/// existing. What remains is whether they still <i>answer</i>, which is what the two probe
/// calls below establish. Today both are in-memory and can only fail by throwing; once either
/// moves behind a database or a shared cache — which is what
/// <c>PRODUCTION_NOTES.md</c> says horizontal scaling requires — this check becomes the place
/// a connection failure is noticed, and the probes stay the same shape.
/// </para>
/// <para>
/// <b>Both probes are reads.</b> A readiness probe runs every few seconds forever; one that
/// wrote would append thousands of meaningless entries to a compliance record and make the
/// audit trail useless as evidence. Nothing here may mutate state.
/// </para>
/// </remarks>
public sealed class DependencyHealthCheck(IEvidenceStore evidenceStore, IAuditLog auditLog)
    : IHealthCheck
{
    /// <summary>The name this check is registered under, and reported as.</summary>
    public const string Name = "dependencies";

    /// <summary>
    /// A tenant that deliberately does not exist, used to probe the audit log.
    /// </summary>
    /// <remarks>
    /// Scoping the probe read to a tenant with no events means a health check never pulls real
    /// audit records into memory, and never returns one. The point is to prove the log answers,
    /// not to look at what is in it.
    /// </remarks>
    private const string ProbeTenant = "__health-probe__";

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A known tenant, so an empty result means the corpus is genuinely gone rather
            // than the tenant simply being unrecognised.
            var probeTenant = EvidenceFixtures.Tenants.Keys.First();
            var documentIds = evidenceStore.DocumentIdsForTenant(probeTenant);
            if (documentIds.Count == 0)
            {
                return Unhealthy("evidence store returned no documents for a known tenant", started);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Exercises the log's read path and its lock without reading anyone's records.
            _ = auditLog.Read(ProbeTenant);

            return HealthCheckResult.Healthy("Evidence store and audit log are responsive.", Data(started));
        }
        catch (OperationCanceledException)
        {
            // The probe itself was cancelled — the caller gave up, not a dependency verdict.
            throw;
        }
        catch (Exception exception)
        {
            // The exception is attached for the log, never for the response body: /health/ready
            // is unauthenticated, and an exception message is exactly the kind of internal
            // detail the error mapping is careful not to hand out. See HealthEndpoints.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "A dependency did not answer.",
                exception: exception,
                data: Data(started));
        }

        static HealthCheckResult Unhealthy(string description, long started) =>
            HealthCheckResult.Unhealthy(description, data: Data(started));

        static IReadOnlyDictionary<string, object> Data(long started) => new Dictionary<string, object>
        {
            ["durationMs"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3),
        };
    }
}
