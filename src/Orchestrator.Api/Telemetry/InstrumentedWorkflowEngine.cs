using System.Diagnostics;
using Orchestrator.Core.Models;
using Orchestrator.Core.Risk;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Api.Telemetry;

/// <summary>
/// Wraps <see cref="IWorkflowEngine"/> and emits metrics around it.
/// </summary>
/// <remarks>
/// <para>
/// The instrumentation lives here rather than inside <c>WorkflowEngine</c> so
/// <c>Orchestrator.Core</c> keeps zero telemetry dependencies alongside its zero ASP.NET ones.
/// The four trust boundaries are domain properties and must stay provable by constructing a
/// plain engine — the 34 domain tests do exactly that, and none of them needed a change for
/// this feature. Observability is something the host adds, not something the domain owes.
/// </para>
/// <para>
/// The decorator is deliberately thin: it calls the real engine, and everything it reads comes
/// from the returned <see cref="WorkflowResult"/>. It makes no decision, changes no output, and
/// re-throws whatever the engine throws.
/// </para>
/// </remarks>
public sealed class InstrumentedWorkflowEngine(IWorkflowEngine inner, WorkflowMetrics metrics)
    : IWorkflowEngine
{
    /// <inheritdoc/>
    public WorkflowResult Run(WorkflowRequest request)
    {
        var started = Stopwatch.GetTimestamp();

        // Not wrapped in try/catch: a failed run is not an assessment and must not be counted
        // as one. Requests refused at the identity boundary are counted by their own rejection
        // metric instead, so the failure path is observable without inventing a risk level for
        // a run that never produced one.
        var result = inner.Run(request);

        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        metrics.RecordAssessment(
            request.TenantId,
            result.RiskLevel,
            elapsedMs,
            injectionDetected: result.Reasons.Contains(RiskEvaluator.InjectionDetectedReason));

        // Only when the caller actually asked for something. See WorkflowMetrics.RecordAction.
        if (!string.IsNullOrEmpty(request.RequestedAction))
        {
            metrics.RecordAction(request.TenantId, result.ActionStatus);
        }

        return result;
    }
}
