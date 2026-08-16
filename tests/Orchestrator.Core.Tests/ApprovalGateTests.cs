using Orchestrator.Core.Models;

namespace Orchestrator.Core.Tests;

/// <summary>
/// The human-in-the-loop gate: high risk executes only against a registered approver for
/// that tenant who is not the requester.
/// </summary>
public sealed class ApprovalGateTests
{
    private const string Action = "markVendorApproved";
    private const string Requester = "approver@tenant-b.example";

    private readonly WorkflowFixture _fixture = new();

    private WorkflowResult RunHighRisk(string? approvedBy = null, string? action = Action) =>
        _fixture.Run("tenant-b", Requester, Role.Approver, requestedAction: action, approvedBy: approvedBy);

    [Fact]
    public void High_risk_action_is_blocked_without_approval()
    {
        var result = RunHighRisk();

        Assert.Equal(RiskLevel.High, result.RiskLevel);
        Assert.True(result.RequiresApproval);
        Assert.Equal(ActionStatus.BlockedPendingApproval, result.ActionStatus);
        Assert.Equal("Do not approve yet.", result.Recommendation);
        Assert.Equal("pending", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void High_risk_action_executes_with_a_registered_approver()
    {
        var result = RunHighRisk(approvedBy: "compliance@tenant-b.example");

        Assert.Equal(ActionStatus.Executed, result.ActionStatus);
        Assert.Equal("approved", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void Approver_from_another_tenant_is_rejected()
    {
        var result = RunHighRisk(approvedBy: "alice@tenant-a.example");

        Assert.Equal(ActionStatus.BlockedPendingApproval, result.ActionStatus);
        Assert.Equal("pending", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void Self_approval_is_rejected()
    {
        var result = RunHighRisk(approvedBy: Requester);

        Assert.Equal(ActionStatus.BlockedPendingApproval, result.ActionStatus);
        Assert.Equal("pending", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void Unrecognised_approver_string_is_rejected()
    {
        var result = RunHighRisk(approvedBy: "totally-made-up@evil.example");

        Assert.Equal(ActionStatus.BlockedPendingApproval, result.ActionStatus);
        Assert.Equal("pending", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void Medium_risk_proceeds_without_approval()
    {
        var result = _fixture.Run(
            "tenant-a", "approver@tenant-a.example", Role.Approver, requestedAction: Action);

        Assert.Equal(RiskLevel.Medium, result.RiskLevel);
        Assert.False(result.RequiresApproval);
        Assert.Equal(ActionStatus.Executed, result.ActionStatus);
        Assert.Equal("approved", _fixture.VendorStatus("tenant-a"));
    }

    [Fact]
    public void Action_outside_the_allow_list_is_refused()
    {
        var result = RunHighRisk(action: "deleteAllVendors");

        Assert.Equal(ActionStatus.BlockedUnknownAction, result.ActionStatus);
        Assert.Equal("pending", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void Advisory_run_reports_not_requested()
    {
        var result = _fixture.Run("tenant-b", "analyst@tenant-b.example", Role.Analyst);

        Assert.Equal(ActionStatus.NotRequested, result.ActionStatus);
        Assert.True(result.RequiresApproval); // high risk still needs sign-off
    }
}
