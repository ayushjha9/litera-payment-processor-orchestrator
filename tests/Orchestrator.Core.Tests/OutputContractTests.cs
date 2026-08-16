using Orchestrator.Core.Models;

namespace Orchestrator.Core.Tests;

/// <summary>
/// The output contract, and the safety invariants it refuses to emit a result without.
/// </summary>
public sealed class OutputContractTests
{
    private readonly WorkflowFixture _fixture = new();

    [Fact]
    public void Result_matches_the_documented_shape()
    {
        var result = _fixture.Run(
            "tenant-b", "approver@tenant-b.example", Role.Approver, requestedAction: "markVendorApproved");

        Assert.True(Enum.IsDefined(result.RiskLevel));
        Assert.True(Enum.IsDefined(result.ActionStatus));
        Assert.NotEmpty(result.Recommendation);
        Assert.NotEmpty(result.Reasons);
        Assert.All(result.Citations, c =>
        {
            Assert.NotEmpty(c.DocumentId);
            Assert.NotEmpty(c.Snippet);
        });
        Assert.Contains("SOC 2 report", result.MissingEvidence);
    }

    [Fact]
    public void Validation_rejects_a_citation_outside_the_tenants_documents()
    {
        var result = new WorkflowResult
        {
            RiskLevel = RiskLevel.Low,
            Recommendation = "ok",
            Reasons = ["fine"],
            Citations = [new Citation("contract-b-002", "leaked")],
            MissingEvidence = [],
            RequiresApproval = false,
            ActionStatus = ActionStatus.NotRequested,
        };

        Assert.Throws<OutputContractException>(
            () => result.Validate(new HashSet<string> { "policy-a-001" }, approvalRecorded: false));
    }

    [Fact]
    public void Validation_rejects_high_risk_that_does_not_require_approval()
    {
        var result = new WorkflowResult
        {
            RiskLevel = RiskLevel.High,
            Recommendation = "ok",
            Reasons = ["missing everything"],
            Citations = [],
            MissingEvidence = ["SOC 2 report"],
            RequiresApproval = false,
            ActionStatus = ActionStatus.NotRequested,
        };

        Assert.Throws<OutputContractException>(
            () => result.Validate(new HashSet<string>(), approvalRecorded: false));
    }

    [Fact]
    public void Validation_rejects_execution_without_a_recorded_approval()
    {
        var result = new WorkflowResult
        {
            RiskLevel = RiskLevel.High,
            Recommendation = "ok",
            Reasons = ["missing everything"],
            Citations = [],
            MissingEvidence = ["SOC 2 report"],
            RequiresApproval = true,
            ActionStatus = ActionStatus.Executed,
        };

        Assert.Throws<OutputContractException>(
            () => result.Validate(new HashSet<string>(), approvalRecorded: false));
    }

    /// <summary>
    /// Snippets are capped so a citation cannot become a channel for dumping a whole
    /// untrusted document into a reviewer's view.
    /// </summary>
    [Fact]
    public void Validation_rejects_an_oversized_citation_snippet()
    {
        var result = new WorkflowResult
        {
            RiskLevel = RiskLevel.Low,
            Recommendation = "ok",
            Reasons = ["fine"],
            Citations = [new Citation("policy-a-001", new string('x', WorkflowResult.SnippetMaxChars + 1))],
            MissingEvidence = [],
            RequiresApproval = false,
            ActionStatus = ActionStatus.NotRequested,
        };

        Assert.Throws<OutputContractException>(
            () => result.Validate(new HashSet<string> { "policy-a-001" }, approvalRecorded: false));
    }
}
