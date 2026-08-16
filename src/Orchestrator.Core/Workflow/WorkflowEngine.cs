using Orchestrator.Core.Actions;
using Orchestrator.Core.Approval;
using Orchestrator.Core.Audit;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;
using Orchestrator.Core.Risk;

namespace Orchestrator.Core.Workflow;

/// <summary>The orchestrator: retrieve → assess → gate → act → audit.</summary>
public interface IWorkflowEngine
{
    /// <summary>Answer a vendor question, and gate any action it asks for.</summary>
    /// <exception cref="UnknownTenantException">If the tenant is not recognised.</exception>
    /// <exception cref="OutputContractException">If a safety invariant would be violated.</exception>
    WorkflowResult Run(WorkflowRequest request);
}

/// <inheritdoc cref="IWorkflowEngine"/>
public sealed class WorkflowEngine(
    IEvidenceStore evidenceStore,
    IRiskEvaluator riskEvaluator,
    IApprovalService approvalService,
    IActionExecutor actionExecutor,
    IAuditLog auditLog) : IWorkflowEngine
{
    private static readonly Dictionary<RiskLevel, string> Recommendations = new()
    {
        [RiskLevel.Low] = "Approve. Evidence supports processing customer payment data.",
        [RiskLevel.Medium] = "Approve with conditions. Close the gaps below before renewal.",
        [RiskLevel.High] = "Do not approve yet.",
    };

    /// <inheritdoc/>
    public WorkflowResult Run(WorkflowRequest request)
    {
        var role = request.Role;
        var roleName = role.ToString().ToLowerInvariant();

        // Collected per-invocation, never shared: concurrent runs must not mix ids into
        // each other's responses.
        var auditIds = new List<string>
        {
            auditLog.Write(
                AuditEventType.WorkflowRun,
                request.TenantId,
                request.UserId,
                roleName,
                new Dictionary<string, object?>
                {
                    ["question"] = request.Question,
                    ["requestedAction"] = request.RequestedAction,
                    ["approvalSupplied"] = !string.IsNullOrEmpty(request.ApprovedBy),
                }),
        };

        var documents = evidenceStore.Search(request.TenantId, EvidenceFixtures.VendorId, request.Question);
        var assessment = riskEvaluator.Evaluate(documents, request.Question);

        var decision = approvalService.RequestOrVerify(
            request.TenantId,
            request.UserId,
            role,
            assessment.RiskLevel,
            request.RequestedAction,
            request.ApprovedBy);

        var actionStatus = decision.ActionStatus;
        if (!string.IsNullOrEmpty(request.RequestedAction))
        {
            auditIds.Add(auditLog.Write(
                AuditEventType.ActionAttempt,
                request.TenantId,
                request.UserId,
                roleName,
                new Dictionary<string, object?>
                {
                    ["action"] = request.RequestedAction,
                    ["riskLevel"] = RiskLevelValue(assessment.RiskLevel),
                    ["requiresApproval"] = decision.RequiresApproval,
                    ["approvalValid"] = decision.Approved,
                    ["approvedBy"] = request.ApprovedBy,
                    ["gateVerdict"] = ActionStatusValue(decision.ActionStatus),
                    ["gateReason"] = decision.Reason,
                }));

            var result = actionExecutor.Execute(
                request.RequestedAction,
                request.TenantId,
                EvidenceFixtures.VendorId,
                role,
                decision);
            actionStatus = result.Status;
        }

        var workflowResult = new WorkflowResult
        {
            RiskLevel = assessment.RiskLevel,
            Recommendation = Recommendation(assessment.RiskLevel, decision),
            Reasons = assessment.Reasons,
            Citations = assessment.Citations,
            MissingEvidence = assessment.MissingEvidence,
            RequiresApproval = decision.RequiresApproval,
            ActionStatus = actionStatus,
        };

        auditIds.Add(auditLog.Write(
            AuditEventType.Decision,
            request.TenantId,
            request.UserId,
            roleName,
            new Dictionary<string, object?>
            {
                ["riskLevel"] = RiskLevelValue(assessment.RiskLevel),
                ["recommendation"] = workflowResult.Recommendation,
                ["actionStatus"] = ActionStatusValue(actionStatus),
                // Ids only — untrusted document text never enters the audit log.
                ["citationDocumentIds"] = assessment.Citations.Select(c => c.DocumentId).ToList(),
                ["missingEvidence"] = assessment.MissingEvidence.ToList(),
            }));

        workflowResult.AuditEventIds = auditIds;

        // Defence in depth: even if a leak were introduced upstream, it cannot escape here.
        workflowResult.Validate(
            evidenceStore.DocumentIdsForTenant(request.TenantId),
            approvalRecorded: decision.Approved);

        return workflowResult;
    }

    private static string Recommendation(RiskLevel riskLevel, ApprovalDecision decision)
    {
        var @base = Recommendations[riskLevel];

        if (decision.ActionStatus is ActionStatus.BlockedUnauthorized)
        {
            return $"{@base} The requesting role is not permitted to execute this action.";
        }

        // Approval does not lower risk. The record should show that a human accepted a
        // documented risk, not that the risk went away.
        if (riskLevel is RiskLevel.High && decision.Approved)
        {
            return "Proceed. A registered approver has accepted the documented risk.";
        }

        return @base;
    }

    // Audit details are written as wire-shaped strings so the log reads the same as the API.
    private static string RiskLevelValue(RiskLevel level) => level.ToString().ToLowerInvariant();

    private static string ActionStatusValue(ActionStatus status) => status switch
    {
        ActionStatus.NotRequested => "not_requested",
        ActionStatus.Executed => "executed",
        ActionStatus.BlockedPendingApproval => "blocked_pending_approval",
        ActionStatus.BlockedUnauthorized => "blocked_unauthorized",
        ActionStatus.BlockedUnknownAction => "blocked_unknown_action",
        _ => throw new OutputContractException($"unmapped action status: {status}"),
    };
}
