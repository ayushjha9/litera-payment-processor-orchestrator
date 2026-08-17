using System.Diagnostics.Metrics;
using System.Text.Json;
using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;

namespace Orchestrator.Api.Telemetry;

/// <summary>
/// The <c>Orchestrator.Workflow</c> meter and every instrument published from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Metric labels are a lower-trust surface than the audit log.</b> The audit trail is
/// tenant-scoped, access-controlled and read by people investigating a specific decision; a
/// metrics backend is typically scraped by an operations team, retained on a different clock,
/// and readable by anyone with a dashboard. So no evidence text, citation snippet, document id
/// or user id is ever a tag — only the low-cardinality dimensions an operator needs to answer
/// "how often, for whom, and how bad". That is a deliberate ceiling on what this class can
/// emit, not an oversight to be relaxed later.
/// </para>
/// <para>
/// High-cardinality tags are also a direct cost problem: every distinct label combination is a
/// separate time series a backend stores and bills for. <see cref="TenantTag"/> exists because
/// the caller-supplied tenant header is unbounded until something validates it.
/// </para>
/// <para>
/// <b>Emission can never fail a request.</b> Every method here swallows its own exceptions —
/// an exporter, meter-listener or tag problem must not turn a successful risk assessment into
/// a 500. Telemetry is an observer of the workflow, never a participant in it.
/// </para>
/// </remarks>
public sealed class WorkflowMetrics
{
    /// <summary>The meter name, as an OpenTelemetry pipeline subscribes to it.</summary>
    public const string MeterName = "Orchestrator.Workflow";

    /// <summary>Tag value standing in for any tenant that is not a recognised one.</summary>
    /// <remarks>
    /// Used wherever the tenant has not (yet) been validated. Emitting the raw header would let
    /// an unauthenticated caller mint an unbounded number of time series simply by varying
    /// <c>X-Tenant-Id</c>, and it would leak probed tenant names into a dashboard — the same
    /// disclosure the unknown-tenant <c>403</c> deliberately avoids.
    /// </remarks>
    public const string UnknownTenant = "unknown";

    private readonly Counter<long> _assessments;
    private readonly Counter<long> _actions;
    private readonly Counter<long> _injectionDetected;
    private readonly Counter<long> _identityRejected;
    private readonly Histogram<double> _assessmentDuration;

    /// <summary>
    /// Creates the meter through <see cref="IMeterFactory"/> so its lifetime is the DI
    /// container's.
    /// </summary>
    /// <remarks>
    /// A <c>new Meter(...)</c> held in a static field is process-global and outlives any single
    /// host, which makes parallel tests observe each other's counters. Going through the factory
    /// means each <c>WebApplicationFactory</c> gets its own meter, and the assertions in
    /// <c>MetricsTests</c> are about the run that produced them.
    /// </remarks>
    public WorkflowMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _assessments = meter.CreateCounter<long>(
            "workflow.assessments.total",
            unit: "{assessment}",
            description: "Risk assessments completed, by tenant and resulting risk level.");

        _actions = meter.CreateCounter<long>(
            "workflow.actions.total",
            unit: "{action}",
            description: "Requested actions, by tenant and the gate's verdict.");

        _injectionDetected = meter.CreateCounter<long>(
            "workflow.injection.detected.total",
            unit: "{detection}",
            description: "Runs where instruction-like text was found in evidence. A spike is an incident, not noise.");

        _identityRejected = meter.CreateCounter<long>(
            "workflow.identity.rejected.total",
            unit: "{rejection}",
            description: "Requests refused at the identity boundary, by reason.");

        _assessmentDuration = meter.CreateHistogram<double>(
            "workflow.assessment.duration",
            unit: "ms",
            description: "Wall-clock duration of a workflow run, by tenant and resulting risk level.");
    }

    /// <summary>Record a completed assessment: count, duration, and any injection detected.</summary>
    /// <param name="tenantId">The validated tenant the run was scoped to.</param>
    /// <param name="riskLevel">The level the evaluator arrived at.</param>
    /// <param name="elapsedMs">Wall-clock duration of the run.</param>
    /// <param name="injectionDetected">Whether the run saw instruction-like text in evidence.</param>
    public void RecordAssessment(string tenantId, RiskLevel riskLevel, double elapsedMs, bool injectionDetected)
    {
        Guard(() =>
        {
            var tenant = TenantTag(tenantId);
            var level = WireValue(riskLevel);

            _assessments.Add(1, Tag("tenantId", tenant), Tag("riskLevel", level));
            _assessmentDuration.Record(elapsedMs, Tag("tenantId", tenant), Tag("riskLevel", level));

            if (injectionDetected)
            {
                _injectionDetected.Add(1, Tag("tenantId", tenant));
            }
        });
    }

    /// <summary>
    /// Record the gate's verdict on a requested action.
    /// </summary>
    /// <remarks>
    /// Only called when the caller actually asked for an action. Counting
    /// <c>not_requested</c> here would make <c>workflow.actions.total</c> a count of runs rather
    /// than of actions, and advisory traffic would swamp the signal an operator wants: how often
    /// a real action was blocked. Total runs are already available from
    /// <c>workflow.assessments.total</c>, so the ratio is still recoverable.
    /// </remarks>
    /// <param name="tenantId">The validated tenant the run was scoped to.</param>
    /// <param name="actionStatus">The gate's verdict.</param>
    public void RecordAction(string tenantId, ActionStatus actionStatus)
    {
        Guard(() => _actions.Add(
            1,
            Tag("tenantId", TenantTag(tenantId)),
            Tag("actionStatus", WireValue(actionStatus))));
    }

    /// <summary>Record a request refused at the identity boundary.</summary>
    /// <param name="reason">A fixed <see cref="IdentityRejection"/> value — never caller text.</param>
    /// <param name="tenantId">
    /// The tenant header as supplied, which may be absent or invented. It is passed through
    /// <see cref="TenantTag"/>, so an unrecognised value becomes <see cref="UnknownTenant"/>.
    /// </param>
    public void RecordIdentityRejection(string reason, string? tenantId)
    {
        Guard(() => _identityRejected.Add(
            1,
            Tag("tenantId", TenantTag(tenantId)),
            Tag("reason", reason)));
    }

    /// <summary>
    /// Collapse any unrecognised tenant to <see cref="UnknownTenant"/>.
    /// </summary>
    /// <remarks>
    /// The allow-list is the fixture tenant registry, which is what the evidence store fails
    /// closed against. Once a real directory replaces the fixtures this should read from the
    /// same source the store does, so the two can never disagree about what "known" means.
    /// </remarks>
    public static string TenantTag(string? tenantId) =>
        tenantId is not null && EvidenceFixtures.Tenants.ContainsKey(tenantId)
            ? tenantId
            : UnknownTenant;

    /// <summary>
    /// The wire spelling of an enum value — <c>blocked_pending_approval</c>, not
    /// <c>BlockedPendingApproval</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately the same naming policy the JSON serializer uses, so a label on a dashboard
    /// and a value in an API response are the same string and can be searched for together.
    /// </remarks>
    private static string WireValue<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!);

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    /// <summary>
    /// Run an emission, absorbing anything it throws.
    /// </summary>
    /// <remarks>
    /// There is no logging here on purpose: a broken exporter would otherwise turn every
    /// request into a log line, and the failure it is reporting is one the caller can do
    /// nothing about. A metrics pipeline that has stopped working shows up as an absence of
    /// data, which is what a scrape-liveness alert is for.
    /// </remarks>
    private static void Guard(Action emit)
    {
        try
        {
            emit();
        }
        catch
        {
            // Telemetry must never fail a request.
        }
    }
}

/// <summary>
/// The fixed set of identity-rejection reasons.
/// </summary>
/// <remarks>
/// Constants rather than free text so the <c>reason</c> label stays bounded. A rejection reason
/// derived from caller input would be an unbounded label, which is the cardinality problem this
/// whole class is careful about.
/// </remarks>
public static class IdentityRejection
{
    /// <summary>A required identity header was absent or blank.</summary>
    public const string MissingHeader = "missing_header";

    /// <summary>The role header was present but not one of the three known roles.</summary>
    public const string UnparseableRole = "unparseable_role";

    /// <summary>The tenant was well-formed but is not a recognised tenant.</summary>
    public const string UnknownTenant = "unknown_tenant";
}
