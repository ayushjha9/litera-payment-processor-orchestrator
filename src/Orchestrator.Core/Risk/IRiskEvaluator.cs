using Orchestrator.Core.Models;

namespace Orchestrator.Core.Risk;

/// <summary>Deterministic, rule-based risk evaluation. No model call, no free-text parsing.</summary>
public interface IRiskEvaluator
{
    /// <summary>
    /// Score the evidence and explain the score with citations.
    /// </summary>
    /// <remarks>Pure and deterministic: the same documents always yield the same assessment.</remarks>
    RiskAssessment Evaluate(IReadOnlyList<Document> documents, string? question = null);
}
