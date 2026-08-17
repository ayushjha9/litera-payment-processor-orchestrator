namespace Orchestrator.Core.Models;

/// <summary>
/// The validated output contract.
/// </summary>
/// <remarks>
/// Internally everything is PascalCase C#. The camelCase wire contract
/// (<c>riskLevel</c>, <c>actionStatus</c>, <c>missingEvidence</c>, ...) exists only at the
/// API boundary, applied by the serializer's naming policy.
/// </remarks>
public sealed class WorkflowResult
{
    /// <summary>
    /// Maximum length of a citation snippet.
    /// </summary>
    /// <remarks>
    /// Counted in UTF-16 units here versus code points in the original Python. Identical for
    /// the ASCII fixture corpus; a corpus with astral-plane characters would truncate slightly
    /// differently.
    /// </remarks>
    public const int SnippetMaxChars = 240;

    public required RiskLevel RiskLevel { get; init; }

    public required string Recommendation { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public required IReadOnlyList<Citation> Citations { get; init; }

    public required IReadOnlyList<string> MissingEvidence { get; init; }

    /// <summary>
    /// Whether the gate demanded a recorded human approval. Describes the gate truthfully:
    /// only high risk sets it, so it can never contradict <see cref="ActionStatus"/>.
    /// </summary>
    public required bool RequiresApproval { get; init; }

    public required ActionStatus ActionStatus { get; init; }

    /// <summary>Ids of the audit events this run wrote, in order. Makes each answer traceable.</summary>
    public IReadOnlyList<string> AuditEventIds { get; set; } = [];

    /// <summary>
    /// Constrain the output shape and assert the safety invariants.
    /// </summary>
    /// <param name="allowedDocumentIds">
    /// The requesting tenant's document set, so a citation can never point outside it.
    /// </param>
    /// <param name="approvalRecorded">Whether a valid approval was actually verified.</param>
    /// <exception cref="OutputContractException">If the contract or an invariant is violated.</exception>
    public void Validate(IReadOnlySet<string> allowedDocumentIds, bool approvalRecorded)
    {
        if (!Enum.IsDefined(RiskLevel))
        {
            throw new OutputContractException($"riskLevel must be a RiskLevel, got {RiskLevel}");
        }

        if (!Enum.IsDefined(ActionStatus))
        {
            throw new OutputContractException($"actionStatus must be an ActionStatus, got {ActionStatus}");
        }

        if (string.IsNullOrEmpty(Recommendation))
        {
            throw new OutputContractException("recommendation must be non-empty");
        }

        if (Reasons.Count == 0)
        {
            throw new OutputContractException("reasons must be non-empty");
        }

        if (Reasons.Any(string.IsNullOrEmpty))
        {
            throw new OutputContractException("every reason must be a non-empty string");
        }

        if (MissingEvidence.Any(string.IsNullOrEmpty))
        {
            throw new OutputContractException("every missingEvidence entry must be a non-empty string");
        }

        foreach (var citation in Citations)
        {
            if (!allowedDocumentIds.Contains(citation.DocumentId))
            {
                throw new OutputContractException(
                    $"citation '{citation.DocumentId}' is outside the requesting tenant's documents");
            }

            if (citation.Snippet.Length > SnippetMaxChars)
            {
                throw new OutputContractException($"citation snippet exceeds {SnippetMaxChars} chars");
            }
        }

        // Safety invariants — the part that actually matters.
        if (RiskLevel is RiskLevel.High && !RequiresApproval)
        {
            throw new OutputContractException("high risk must require approval");
        }

        if (ActionStatus is ActionStatus.Executed && RequiresApproval && !approvalRecorded)
        {
            throw new OutputContractException("action executed while approval was required but not recorded");
        }
    }
}
