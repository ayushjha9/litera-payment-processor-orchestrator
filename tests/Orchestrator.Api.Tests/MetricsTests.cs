using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Orchestrator.Api.Telemetry;
using Orchestrator.Core.Fixtures;

namespace Orchestrator.Api.Tests;

/// <summary>
/// The counters increment with the right tags, and — the part that matters — nothing
/// caller-derived beyond the tenant ever becomes a label.
/// </summary>
/// <remarks>
/// Each test builds its own <see cref="ApiFactory"/>. The meter is created through
/// <c>IMeterFactory</c> and therefore belongs to that host's container, so counters cannot leak
/// between tests running in parallel.
/// </remarks>
public sealed class MetricsTests
{
    private const string Assessments = "workflow.assessments.total";
    private const string Actions = "workflow.actions.total";
    private const string Injection = "workflow.injection.detected.total";
    private const string IdentityRejected = "workflow.identity.rejected.total";
    private const string Duration = "workflow.assessment.duration";

    [Fact]
    public async Task Medium_risk_advisory_run_counts_an_assessment_and_no_action()
    {
        using var harness = new MetricsHarness();
        using var assessments = harness.Collect<long>(Assessments);
        using var actions = harness.Collect<long>(Actions);
        using var duration = harness.Collect<double>(Duration);

        var client = harness.Factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");
        Assert.Equal(HttpStatusCode.OK, (await ApiFactory.Run(client)).StatusCode);

        var measurement = Assert.Single(assessments.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal("tenant-a", measurement.Tags["tenantId"]);
        Assert.Equal("medium", measurement.Tags["riskLevel"]);

        // No action was requested, so workflow.actions.total stays a count of actions rather
        // than becoming a second, noisier count of runs.
        Assert.Empty(actions.GetMeasurementSnapshot());

        var timing = Assert.Single(duration.GetMeasurementSnapshot());
        Assert.True(timing.Value >= 0);
        Assert.Equal("medium", timing.Tags["riskLevel"]);
        Assert.Equal("tenant-a", timing.Tags["tenantId"]);
    }

    [Fact]
    public async Task Blocked_pending_approval_run_is_tagged_with_the_gate_verdict()
    {
        using var harness = new MetricsHarness();
        using var actions = harness.Collect<long>(Actions);

        var client = harness.Factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");
        await ApiFactory.Run(client, requestedAction: "markVendorApproved");

        var measurement = Assert.Single(actions.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal("blocked_pending_approval", measurement.Tags["actionStatus"]);
        Assert.Equal("tenant-b", measurement.Tags["tenantId"]);
    }

    [Fact]
    public async Task Executed_run_is_tagged_executed_and_still_counts_as_high_risk()
    {
        using var harness = new MetricsHarness();
        using var actions = harness.Collect<long>(Actions);
        using var assessments = harness.Collect<long>(Assessments);

        var client = harness.Factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");
        await ApiFactory.Run(
            client,
            requestedAction: "markVendorApproved",
            approvedBy: "compliance@tenant-b.example");

        var action = Assert.Single(actions.GetMeasurementSnapshot());
        Assert.Equal("executed", action.Tags["actionStatus"]);

        // Approval does not lower risk, and the metric says so too: an executed high-risk
        // action is still counted as high. A dashboard should show that someone accepted a
        // documented risk, not that the risk went away.
        var assessment = Assert.Single(assessments.GetMeasurementSnapshot());
        Assert.Equal("high", assessment.Tags["riskLevel"]);
    }

    [Fact]
    public async Task Injection_is_counted_for_the_tenant_whose_evidence_carries_it()
    {
        using var harness = new MetricsHarness();
        using var injection = harness.Collect<long>(Injection);

        // tenant-b's contract carries the injected addendum; tenant-a's does not.
        await ApiFactory.Run(harness.Factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst"));
        await ApiFactory.Run(harness.Factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver"));

        var measurement = Assert.Single(injection.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal("tenant-b", measurement.Tags["tenantId"]);
    }

    [Theory]
    [InlineData("tenant-a", "superadmin", "unparseable_role", "tenant-a")]
    [InlineData("", "analyst", "missing_header", WorkflowMetrics.UnknownTenant)]
    [InlineData("tenant-zzz", "analyst", "unknown_tenant", WorkflowMetrics.UnknownTenant)]
    public async Task Identity_rejections_are_counted_by_reason(
        string tenantId, string role, string expectedReason, string expectedTenantTag)
    {
        using var harness = new MetricsHarness();
        using var rejections = harness.Collect<long>(IdentityRejected);

        var client = harness.Factory.CreateClient();
        if (tenantId.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        }

        client.DefaultRequestHeaders.Add("X-User-Id", "someone@example.test");
        client.DefaultRequestHeaders.Add("X-Role", role);

        await ApiFactory.Run(client);

        var measurement = Assert.Single(rejections.GetMeasurementSnapshot());
        Assert.Equal(expectedReason, measurement.Tags["reason"]);
        Assert.Equal(expectedTenantTag, measurement.Tags["tenantId"]);
    }

    [Fact]
    public async Task An_invented_tenant_cannot_mint_time_series()
    {
        using var harness = new MetricsHarness();
        using var rejections = harness.Collect<long>(IdentityRejected);

        // An unauthenticated caller varying X-Tenant-Id would otherwise create one series per
        // value — a cost problem and a disclosure of which tenants were probed.
        foreach (var invented in new[] { "tenant-aaa", "tenant-bbb", "tenant-ccc" })
        {
            var client = harness.Factory.ClientFor(invented, "someone@example.test", "analyst");
            await ApiFactory.Run(client);
        }

        var tags = rejections.GetMeasurementSnapshot()
            .Select(m => (string?)m.Tags["tenantId"])
            .Distinct()
            .ToList();

        Assert.Equal([WorkflowMetrics.UnknownTenant], tags);
    }

    [Fact]
    public async Task No_metric_tag_carries_document_text_snippets_or_user_ids()
    {
        using var harness = new MetricsHarness();

        // Exercise every emission path, so anything that could leak has been recorded.
        var analyst = harness.Factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");
        var approver = harness.Factory.ClientFor("tenant-b", "approver@tenant-b.example", "approver");
        await ApiFactory.Run(analyst);
        await ApiFactory.Run(approver, requestedAction: "markVendorApproved");
        await ApiFactory.Run(
            approver, requestedAction: "markVendorApproved", approvedBy: "compliance@tenant-b.example");
        await ApiFactory.Run(harness.Factory.ClientFor("tenant-zzz", "someone@example.test", "analyst"));

        var scrape = await harness.Factory.CreateClient().GetStringAsync("/metrics");

        // Asserted against the exported text rather than in-process tags: this is the surface an
        // operator, and anyone who can reach the scrape endpoint, actually sees.
        string[] forbidden =
        [
            EvidenceFixtures.InjectedText,      // untrusted evidence prose
            "ignore all previous instructions", // the injected sentence, in case of truncation
            "SOC 2 Type II report",             // citation snippet text
            "policy-a-001", "contract-a-002",   // document ids
            "policy-b-001", "contract-b-002",
            "analyst@tenant-a.example",         // user ids
            "approver@tenant-b.example",
            "compliance@tenant-b.example",      // approver identity
            "Can we approve Vendor X",          // the caller's question
        ];

        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, scrape, StringComparison.OrdinalIgnoreCase);
        }

        // The scrape is not vacuously clean — the instruments really are there.
        Assert.Contains("workflow_assessments_total", scrape, StringComparison.Ordinal);
        Assert.Contains("tenantId=\"tenant-a\"", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_meter_cannot_fail_a_request()
    {
        using var harness = new MetricsHarness();
        var client = harness.Factory.ClientFor("tenant-a", "analyst@tenant-a.example", "analyst");

        // A MeterListener callback that throws stands in for a misbehaving exporter. The
        // workflow must be indifferent to it: telemetry observes the request, it does not
        // participate in it.
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == WorkflowMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>(
            (_, _, _, _) => throw new InvalidOperationException("exporter is down"));
        listener.Start();

        var response = await ApiFactory.Run(client, requestedAction: "markVendorApproved");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// An <see cref="ApiFactory"/> plus the meter factory its instruments were created from.
/// </summary>
/// <remarks>
/// Resolving <see cref="WorkflowMetrics"/> eagerly forces the meter and its instruments into
/// existence before any collector is attached, so a collector never misses the first
/// measurement to a lazily-created instrument.
/// </remarks>
file sealed class MetricsHarness : IDisposable
{
    public ApiFactory Factory { get; } = new();

    private readonly IMeterFactory _meterFactory;

    public MetricsHarness()
    {
        _ = Factory.Services.GetRequiredService<WorkflowMetrics>();
        _meterFactory = Factory.Services.GetRequiredService<IMeterFactory>();
    }

    public MetricCollector<T> Collect<T>(string instrumentName) where T : struct =>
        new(_meterFactory, WorkflowMetrics.MeterName, instrumentName);

    public void Dispose() => Factory.Dispose();
}
