using Orchestrator.Core.Models;

namespace Orchestrator.Core.Approval;

/// <summary>Authorization and the human-in-the-loop approval gate.</summary>
public interface IApprovalService
{
    /// <summary>
    /// The allow-list of executable actions and the roles permitted to run them.
    /// </summary>
    /// <remarks>An allow-list, not a deny-list: an action nobody wrote a rule for is refused.</remarks>
    IReadOnlyDictionary<string, IReadOnlySet<Role>> RiskyActions { get; }

    /// <summary>
    /// An approval counts only if the approver belongs to <i>this</i> tenant and is not the
    /// person making the request (no self-approval).
    /// </summary>
    bool IsValidApprover(string tenantId, string? approvedBy, string userId);

    /// <summary>Decide whether the requested action may proceed.</summary>
    ApprovalDecision RequestOrVerify(
        string tenantId,
        string userId,
        Role role,
        RiskLevel riskLevel,
        string? requestedAction,
        string? approvedBy);
}
