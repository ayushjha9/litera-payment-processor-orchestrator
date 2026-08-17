using Bunit;
using Orchestrator.Ui.Components.Contracts;
using Orchestrator.Ui.Components.Display;

namespace Orchestrator.Ui.Tests;

/// <summary>The display components render the decision faithfully.</summary>
public sealed class DisplayComponentTests : BunitContext
{
    [Theory]
    [InlineData(RiskLevelDto.Low, "low", "Low")]
    [InlineData(RiskLevelDto.Medium, "medium", "Medium")]
    [InlineData(RiskLevelDto.High, "high", "High")]
    public void Risk_badge_states_the_level_in_text(RiskLevelDto level, string wire, string label)
    {
        var component = Render<RiskBadge>(p => p.Add(c => c.Level, level));
        var badge = component.Find(".oc-badge");

        Assert.Equal(wire, badge.GetAttribute("data-risk"));

        // Spelled out, not conveyed by colour alone — a compliance decision must not depend on
        // distinguishing amber from red.
        Assert.Contains(label, component.Find(".oc-badge__value").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActionStatusDto.NotRequested, "not_requested", "Not requested")]
    [InlineData(ActionStatusDto.Executed, "executed", "Executed")]
    [InlineData(ActionStatusDto.BlockedPendingApproval, "blocked_pending_approval", "Blocked pending approval")]
    [InlineData(ActionStatusDto.BlockedUnauthorized, "blocked_unauthorized", "Blocked unauthorized")]
    [InlineData(ActionStatusDto.BlockedUnknownAction, "blocked_unknown_action", "Blocked unknown action")]
    public void Action_status_badge_uses_the_wire_spelling(
        ActionStatusDto status, string wire, string label)
    {
        var component = Render<ActionStatusBadge>(p => p.Add(c => c.Status, status));

        // The same string the API returns and the metrics label carries, so a value on screen
        // can be searched for in a log or a dashboard without translation.
        Assert.Equal(wire, component.Find(".oc-badge").GetAttribute("data-action-status"));
        Assert.Contains(label, component.Find(".oc-badge__value").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Blocked_states_are_styled_as_decisions_not_errors()
    {
        var blocked = Render<ActionStatusBadge>(p => p
            .Add(c => c.Status, ActionStatusDto.BlockedPendingApproval));

        // The API returns 200 for this. Styling it as a failure would invite a reviewer to
        // retry a policy decision as if it were a transport problem.
        Assert.Contains("oc-badge--action-blocked_pending_approval", blocked.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("error", blocked.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Result_view_shows_every_reason_citation_and_audit_id()
    {
        var component = Render<WorkflowResultView>(p => p.Add(c => c.Result, HighRiskBlocked));

        var text = component.Find(".oc-result").TextContent;
        Assert.Contains("No SOC 2 evidence found.", text, StringComparison.Ordinal);
        Assert.Contains("Contract lacks breach notification language.", text, StringComparison.Ordinal);
        Assert.Contains("SOC 2 report", text, StringComparison.Ordinal);

        Assert.Equal(2, component.FindAll(".oc-citation").Count);
        Assert.Equal(3, component.FindAll(".oc-result__audit-ids li").Count);
    }

    [Fact]
    public void An_executed_high_risk_result_still_reports_high_with_reasons_intact()
    {
        var executed = HighRiskBlocked with
        {
            ActionStatus = ActionStatusDto.Executed,
            Recommendation = "Proceed. A registered approver has accepted the documented risk.",
        };

        var component = Render<WorkflowResultView>(p => p.Add(c => c.Result, executed));

        // Approval does not lower risk. The UI must not soften that: the record should show
        // that a human accepted a documented risk, not that the risk went away.
        Assert.Equal("high", component.Find(".oc-result").GetAttribute("data-risk"));
        Assert.Contains(
            "No SOC 2 evidence found.", component.Find(".oc-result").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_approval_gate_is_shown_only_when_approval_is_required()
    {
        var gated = Render<WorkflowResultView>(p => p.Add(c => c.Result, HighRiskBlocked));
        Assert.NotNull(gated.Find("[data-requires-approval]"));

        var advisory = Render<WorkflowResultView>(p => p.Add(c => c.Result, MediumRiskAdvisory));
        Assert.Empty(advisory.FindAll("[data-requires-approval]"));
    }

    [Fact]
    public void Evidence_flags_state_absence_in_words()
    {
        var component = Render<EvidenceTable>(p => p.Add(c => c.Documents, [TenantBContract]));

        // "No SOC 2 report" is the most consequential fact this UI displays. It must not be
        // communicable only by the absence of a tick.
        var absent = component.FindAll(".oc-flag--absent");
        Assert.Equal(3, absent.Count);
        Assert.All(absent, flag =>
            Assert.Contains("not evidenced", flag.TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_table_renders_ids_and_types_and_offers_no_tenant_control()
    {
        var component = Render<AuditTable>(p => p.Add(c => c.Events, [Event("evt-000001", "workflow_run")]));

        Assert.Contains("evt-000001", component.Markup, StringComparison.Ordinal);
        Assert.Contains("workflow_run", component.Markup, StringComparison.Ordinal);

        // The API scopes /api/v1/audit to the caller's tenant and has no tenant parameter. A
        // control here would imply a capability that does not and should not exist.
        Assert.Empty(component.FindAll("select"));
        Assert.Empty(component.FindAll("input"));
    }

    [Fact]
    public void Empty_collections_say_so_rather_than_rendering_nothing()
    {
        // An empty region and a missing region look identical; only one of them is a statement.
        Assert.Contains("No audit events", Render<AuditTable>().Markup, StringComparison.Ordinal);
        Assert.Contains("No citations", Render<CitationList>().Markup, StringComparison.Ordinal);
        Assert.Contains("No evidence on file", Render<EvidenceTable>().Markup, StringComparison.Ordinal);
    }

    private static WorkflowResponseDto HighRiskBlocked => new(
        RiskLevelDto.High,
        "Do not approve yet.",
        [
            "No SOC 2 evidence found.",
            "Contract lacks breach notification language.",
            "No documented data retention schedule on file.",
        ],
        [
            new CitationDto("policy-b-001", "Payment data vendors require security evidence."),
            new CitationDto("contract-b-002", "...vendor-submitted addendum..."),
        ],
        ["SOC 2 report", "breach notification clause", "data retention schedule"],
        RequiresApproval: true,
        ActionStatusDto.BlockedPendingApproval,
        ["evt-000001", "evt-000002", "evt-000003"]);

    private static WorkflowResponseDto MediumRiskAdvisory => new(
        RiskLevelDto.Medium,
        "Approve with conditions. Close the gaps below before renewal.",
        ["No documented data retention schedule on file."],
        [new CitationDto("policy-a-001", "Payment data vendors require security evidence.")],
        ["data retention schedule"],
        RequiresApproval: false,
        ActionStatusDto.NotRequested,
        ["evt-000001", "evt-000002"]);

    private static DocumentDto TenantBContract => new(
        "contract-b-002", "tenant-b", "vendor-x", "contract",
        "Vendor X order form and addendum (Contoso)",
        "Vendor X confirms encryption in transit (TLS 1.2).",
        HasSoc2: false, HasEncryption: true,
        HasBreachNotification: false, HasRetentionSchedule: false);

    private static AuditEventDto Event(string id, string type) => new(
        id, "2026-08-17T10:00:00.0000000+00:00", type,
        "tenant-b", "approver@tenant-b.example", "approver",
        new Dictionary<string, System.Text.Json.JsonElement>());
}
