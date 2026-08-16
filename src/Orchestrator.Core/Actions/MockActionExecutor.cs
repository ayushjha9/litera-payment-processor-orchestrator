using Orchestrator.Core.Approval;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Actions;

/// <summary>The one risky action, mocked. Nothing here leaves the process.</summary>
public interface IActionExecutor
{
    /// <summary>Perform the action, re-checking the gate rather than trusting the caller.</summary>
    ActionResult Execute(
        string action,
        string tenantId,
        string vendorId,
        Role role,
        ApprovalDecision decision);
}

/// <inheritdoc cref="IActionExecutor"/>
public sealed class MockActionExecutor(IApprovalService approvalService, IVendorStateStore vendorState)
    : IActionExecutor
{
    /// <summary>
    /// Perform the action, re-checking the gate rather than trusting the caller.
    /// </summary>
    /// <remarks>
    /// A second check here is deliberate: the gate and the effect are separate modules, and
    /// the effect should not be reachable by a future caller that forgets to consult the gate.
    /// </remarks>
    public ActionResult Execute(
        string action,
        string tenantId,
        string vendorId,
        Role role,
        ApprovalDecision decision)
    {
        if (!approvalService.RiskyActions.TryGetValue(action, out var permittedRoles))
        {
            return new ActionResult(ActionStatus.BlockedUnknownAction, $"Unknown action '{action}'.");
        }

        if (!permittedRoles.Contains(role))
        {
            return new ActionResult(
                ActionStatus.BlockedUnauthorized,
                $"Role '{role.ToString().ToLowerInvariant()}' may not execute '{action}'.");
        }

        if (decision.ActionStatus is not ActionStatus.Executed)
        {
            return new ActionResult(decision.ActionStatus, decision.Reason);
        }

        if (decision.RequiresApproval && !decision.Approved)
        {
            return new ActionResult(
                ActionStatus.BlockedPendingApproval,
                "Approval required but not recorded.");
        }

        vendorState.MarkApproved(tenantId, vendorId);
        return new ActionResult(
            ActionStatus.Executed,
            $"{action} recorded for {vendorId}.",
            new Dictionary<string, string>
            {
                ["action"] = action,
                ["tenantId"] = tenantId,
                ["vendorId"] = vendorId,
                ["status"] = "approved",
            });
    }
}
