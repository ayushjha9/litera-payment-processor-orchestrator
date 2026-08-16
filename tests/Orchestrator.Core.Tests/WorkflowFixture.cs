using Orchestrator.Core.Actions;
using Orchestrator.Core.Approval;
using Orchestrator.Core.Audit;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Models;
using Orchestrator.Core.Risk;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Tests;

/// <summary>
/// A self-contained engine with its own audit log and vendor state.
/// </summary>
/// <remarks>
/// The Python suite used an autouse fixture to reset process-global state between tests.
/// Here each test constructs its own instances instead, which is both closer to how the
/// service composes the graph and safe under xUnit's parallel test-class execution — shared
/// mutable singletons across parallel tests produce flaky failures that read as real bugs.
/// </remarks>
public sealed class WorkflowFixture
{
    public const string Question = "Can we approve Vendor X to process customer payment data?";

    public IAuditLog AuditLog { get; } = new InMemoryAuditLog();

    public IVendorStateStore VendorState { get; } = new InMemoryVendorStateStore();

    public IEvidenceStore EvidenceStore { get; } = new InMemoryEvidenceStore();

    public IRiskEvaluator RiskEvaluator { get; } = new RiskEvaluator();

    public IApprovalService ApprovalService { get; }

    public IWorkflowEngine Engine { get; }

    public WorkflowFixture()
    {
        ApprovalService = new ApprovalService();
        var executor = new MockActionExecutor(ApprovalService, VendorState);
        Engine = new WorkflowEngine(EvidenceStore, RiskEvaluator, ApprovalService, executor, AuditLog);
    }

    /// <summary>Run the workflow, defaulting the fields most tests do not vary.</summary>
    public WorkflowResult Run(
        string tenantId,
        string userId,
        Role role,
        string? question = null,
        string? requestedAction = null,
        string? approvedBy = null) =>
        Engine.Run(new WorkflowRequest
        {
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            Question = question ?? Question,
            RequestedAction = requestedAction,
            ApprovedBy = approvedBy,
        });

    public string VendorStatus(string tenantId) =>
        VendorState.Status(tenantId, Fixtures.EvidenceFixtures.VendorId);
}
