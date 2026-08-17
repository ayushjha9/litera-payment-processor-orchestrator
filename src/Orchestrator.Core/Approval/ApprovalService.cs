using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Approval;

/// <inheritdoc cref="IApprovalService"/>
public sealed class ApprovalService : IApprovalService
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, IReadOnlySet<Role>> RiskyActions { get; } =
        new Dictionary<string, IReadOnlySet<Role>>
        {
            ["markVendorApproved"] = new HashSet<Role> { Role.Approver },
        };

    /// <inheritdoc/>
    public bool IsValidApprover(string tenantId, string? approvedBy, string userId)
    {
        if (string.IsNullOrEmpty(approvedBy))
        {
            return false;
        }

        // No self-approval, however senior the requester.
        if (approvedBy == userId)
        {
            return false;
        }

        return EvidenceFixtures.Approvers.TryGetValue(tenantId, out var approvers)
               && approvers.Contains(approvedBy);
    }

    /// <summary>
    /// Decide whether the requested action may proceed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only HIGH risk blocks. MEDIUM returns a caution in the recommendation but does not
    /// gate, so <c>requiresApproval</c> stays a truthful statement about the gate rather than
    /// an advisory flag that contradicts <c>actionStatus</c>.
    /// </para>
    /// <para>
    /// Authorization is checked <i>before</i> approval, so an unauthorized role is refused
    /// even while holding a valid approval.
    /// </para>
    /// </remarks>
    public ApprovalDecision RequestOrVerify(
        string tenantId,
        string userId,
        Role role,
        RiskLevel riskLevel,
        string? requestedAction,
        string? approvedBy)
    {
        var requiresApproval = riskLevel is RiskLevel.High;
        var approved = IsValidApprover(tenantId, approvedBy, userId);

        if (string.IsNullOrEmpty(requestedAction))
        {
            return new ApprovalDecision(
                requiresApproval,
                approved,
                ActionStatus.NotRequested,
                "No action requested; advisory answer only.");
        }

        if (!RiskyActions.TryGetValue(requestedAction, out var permittedRoles))
        {
            return new ApprovalDecision(
                requiresApproval,
                approved,
                ActionStatus.BlockedUnknownAction,
                $"Action '{requestedAction}' is not on the allow-list of executable actions.");
        }

        if (!permittedRoles.Contains(role))
        {
            return new ApprovalDecision(
                requiresApproval,
                approved,
                ActionStatus.BlockedUnauthorized,
                $"Role '{RoleName(role)}' is not permitted to execute '{requestedAction}'.");
        }

        if (requiresApproval && !approved)
        {
            var detail = !string.IsNullOrEmpty(approvedBy) && approvedBy == userId
                ? "self-approval is not permitted"
                : !string.IsNullOrEmpty(approvedBy)
                    ? $"'{approvedBy}' is not a registered approver for '{tenantId}'"
                    : "no approval was supplied";

            return new ApprovalDecision(
                RequiresApproval: true,
                Approved: false,
                ActionStatus.BlockedPendingApproval,
                $"High-risk action requires a recorded human approval: {detail}.");
        }

        return new ApprovalDecision(
            requiresApproval,
            approved,
            ActionStatus.Executed,
            approved
                ? $"Approved by '{approvedBy}'."
                : $"{RiskLevelName(riskLevel)}-risk action does not require approval.");
    }

    private static string RoleName(Role role) => role.ToString().ToLowerInvariant();

    // Matches Python's `risk_level.value.capitalize()`: "medium" -> "Medium".
    private static string RiskLevelName(RiskLevel riskLevel) => riskLevel.ToString();
}
