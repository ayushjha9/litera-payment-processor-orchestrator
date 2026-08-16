namespace Orchestrator.Core.Models;

/// <summary>
/// How much risk the evidence supports. Only <see cref="High"/> gates an action;
/// <see cref="Medium"/> returns a caution in the recommendation.
/// </summary>
/// <remarks>
/// Serialized as <c>low</c> / <c>medium</c> / <c>high</c> by the snake_case_lower enum
/// naming policy configured in the API's JSON options.
/// </remarks>
public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>
/// The outcome of the requested action, if one was requested.
/// </summary>
/// <remarks>
/// Serialized snake_case: <c>not_requested</c>, <c>executed</c>,
/// <c>blocked_pending_approval</c>, <c>blocked_unauthorized</c>,
/// <c>blocked_unknown_action</c>.
/// </remarks>
public enum ActionStatus
{
    NotRequested,
    Executed,
    BlockedPendingApproval,
    BlockedUnauthorized,
    BlockedUnknownAction,
}

/// <summary>
/// The caller's role. Resolved from the authenticated principal at the API edge, never
/// asserted by the caller in a request body.
/// </summary>
public enum Role
{
    Viewer,
    Analyst,
    Approver,
}
