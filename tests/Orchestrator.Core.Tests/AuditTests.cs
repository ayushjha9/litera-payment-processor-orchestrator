using Orchestrator.Core.Audit;
using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Tests;

/// <summary>
/// Every run is traceable, every action attempt is recorded whether allowed or blocked, and
/// untrusted document text never reaches the log.
/// </summary>
public sealed class AuditTests
{
    private const string Action = "markVendorApproved";
    private const string Requester = "approver@tenant-b.example";

    private readonly WorkflowFixture _fixture = new();

    private WorkflowResult RunAction(string? approvedBy = null) =>
        _fixture.Run("tenant-b", Requester, Role.Approver, requestedAction: Action, approvedBy: approvedBy);

    private static string Render(IReadOnlyDictionary<string, object?> details) =>
        string.Join("|", details.Select(kv =>
            $"{kv.Key}={(kv.Value is IEnumerable<string> list ? string.Join(",", list) : kv.Value)}"));

    [Fact]
    public void Every_workflow_run_writes_an_audit_event()
    {
        _fixture.Run("tenant-a", "analyst@tenant-a.example", Role.Analyst);

        var types = _fixture.AuditLog.Read().Select(e => e.EventType).ToList();
        Assert.Contains(AuditEventType.WorkflowRun, types);
        Assert.Contains(AuditEventType.Decision, types);
    }

    [Fact]
    public void Blocked_action_still_writes_an_action_attempt_event()
    {
        RunAction();

        var attempts = _fixture.AuditLog.Read()
            .Where(e => e.EventType is AuditEventType.ActionAttempt).ToList();

        Assert.Single(attempts);
        Assert.Equal(Action, attempts[0].Details["action"]);
        Assert.Equal("blocked_pending_approval", attempts[0].Details["gateVerdict"]);
        Assert.Equal(false, attempts[0].Details["approvalValid"]);
    }

    [Fact]
    public void Executed_action_writes_an_action_attempt_event()
    {
        RunAction(approvedBy: "compliance@tenant-b.example");

        var attempts = _fixture.AuditLog.Read()
            .Where(e => e.EventType is AuditEventType.ActionAttempt).ToList();

        Assert.Single(attempts);
        Assert.Equal(true, attempts[0].Details["approvalValid"]);

        var decision = _fixture.AuditLog.Read().Last(e => e.EventType is AuditEventType.Decision);
        Assert.Equal("executed", decision.Details["actionStatus"]);
    }

    [Fact]
    public void Returned_audit_ids_match_the_log()
    {
        var result = RunAction();

        var logged = _fixture.AuditLog.Read().Select(e => e.EventId).ToList();
        Assert.Equal(logged, result.AuditEventIds);
    }

    [Fact]
    public void Audit_events_carry_document_ids_not_untrusted_text()
    {
        RunAction();

        foreach (var e in _fixture.AuditLog.Read())
        {
            Assert.DoesNotContain(EvidenceFixtures.InjectedText, Render(e.Details));
        }

        var decision = _fixture.AuditLog.Read().Last(e => e.EventType is AuditEventType.Decision);
        var citedIds = Assert.IsAssignableFrom<IEnumerable<string>>(decision.Details["citationDocumentIds"]);
        Assert.Contains("contract-b-002", citedIds);
    }

    [Fact]
    public void Audit_events_are_scoped_to_the_requesting_tenant()
    {
        RunAction();
        _fixture.Run("tenant-a", "analyst@tenant-a.example", Role.Analyst);

        var tenantA = _fixture.AuditLog.Read("tenant-a");
        Assert.Equal(["tenant-a"], tenantA.Select(e => e.TenantId).Distinct());
        Assert.All(tenantA, e => Assert.DoesNotContain("tenant-b", Render(e.Details)));
    }
}
