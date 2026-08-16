namespace Orchestrator.Core.Models;

/// <summary>
/// A quoted excerpt backing a stated reason.
/// </summary>
/// <remarks>
/// Quoting untrusted prose back as a citation is the one legitimate way document text
/// leaves the system: it is presented to a human <i>as</i> a quotation, attributed to a
/// document id, never restated as a system-authored conclusion.
/// </remarks>
public sealed record Citation(string DocumentId, string Snippet);

/// <summary>The outcome of scoring one tenant's evidence for one vendor.</summary>
/// <remarks>
/// Equality is <b>structural over the collections</b>, not the reference equality a record
/// would give them by default. Evaluation is pure and deterministic, and the test that pins
/// that property compares two independent evaluations of the same documents — which is only
/// meaningful if equality looks at the contents.
/// </remarks>
public sealed record RiskAssessment(
    RiskLevel RiskLevel,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<Citation> Citations,
    IReadOnlyList<string> MissingEvidence)
{
    /// <inheritdoc/>
    public bool Equals(RiskAssessment? other) =>
        other is not null
        && RiskLevel == other.RiskLevel
        && Reasons.SequenceEqual(other.Reasons)
        && Citations.SequenceEqual(other.Citations)
        && MissingEvidence.SequenceEqual(other.MissingEvidence);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RiskLevel);
        foreach (var reason in Reasons) hash.Add(reason);
        foreach (var citation in Citations) hash.Add(citation);
        foreach (var missing in MissingEvidence) hash.Add(missing);
        return hash.ToHashCode();
    }
}

/// <summary>The gate's verdict on a requested action.</summary>
public sealed record ApprovalDecision(
    bool RequiresApproval,
    bool Approved,
    ActionStatus ActionStatus,
    string Reason);

/// <summary>The result of attempting the effect itself.</summary>
public sealed record ActionResult(
    ActionStatus Status,
    string Detail,
    IReadOnlyDictionary<string, string>? Receipt = null);
