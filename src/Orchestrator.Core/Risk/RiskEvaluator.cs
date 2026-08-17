using System.Text.RegularExpressions;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Risk;

/// <summary>
/// Deterministic, rule-based risk evaluation. No model call, no free-text parsing.
/// </summary>
/// <remarks>
/// <para>
/// The trust boundary lives here. Risk is computed purely from the structured <c>Has*</c>
/// flags on each <see cref="Document"/>. Document prose is read for exactly two purposes,
/// neither of which can influence a decision in the attacker's favour:
/// </para>
/// <list type="number">
///   <item><description>to quote a snippet back as a citation, and</description></item>
///   <item><description>to <i>detect</i> instruction-like content, which can only ever raise risk.</description></item>
/// </list>
/// <para>
/// There is no code path where document text lowers a risk score, opens the approval gate,
/// or reaches an interpreter.
/// </para>
/// </remarks>
public sealed partial class RiskEvaluator : IRiskEvaluator
{
    private const int HighThreshold = 3;
    private const int MediumThreshold = 1;

    /// <summary>
    /// The reason emitted when instruction-like text is found in evidence.
    /// </summary>
    /// <remarks>
    /// Public so a caller can recognise the signal without re-running the detector or
    /// matching a copied string literal. It is deliberately <i>not</i> a new field on
    /// <see cref="RiskAssessment"/>: the response contract is byte-compatible with the
    /// original implementation and should stay that way.
    /// </remarks>
    public const string InjectionDetectedReason =
        "Evidence contains instruction-like text addressed to an automated reviewer. " +
        "Treated as untrusted content and as a tampering signal; it does not affect the decision.";

    // Phrases that look like an instruction aimed at an automated reviewer. Matching one is a
    // tampering signal, not a command. Order is significant: the first match wins, and the
    // matched text anchors the citation excerpt.
    private static readonly Regex[] InjectionPatterns =
    [
        IgnorePreviousInstructions(),
        DisregardPolicy(),
        MarkVendorApproved(),
        RegardlessOfRisk(),
        ReportRiskLevel(),
    ];

    [GeneratedRegex(@"ignore (all |any )?(previous|prior|above) instructions", RegexOptions.IgnoreCase)]
    private static partial Regex IgnorePreviousInstructions();

    [GeneratedRegex(@"disregard (the )?(risk|policy|policies|instructions)", RegexOptions.IgnoreCase)]
    private static partial Regex DisregardPolicy();

    [GeneratedRegex(@"mark (this|the) vendor (as )?approved", RegexOptions.IgnoreCase)]
    private static partial Regex MarkVendorApproved();

    [GeneratedRegex(@"regardless of risk", RegexOptions.IgnoreCase)]
    private static partial Regex RegardlessOfRisk();

    [GeneratedRegex(@"report risk level (low|medium)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportRiskLevel();

    /// <summary>A missing control: how it is detected, what it costs, and how it is explained.</summary>
    private sealed record Control(
        Func<Document, bool> IsSatisfiedBy,
        int Weight,
        string Reason,
        string MissingEvidenceLabel);

    private static readonly Control[] Controls =
    [
        new(d => d.HasSoc2, 2, "No SOC 2 evidence found.", "SOC 2 report"),
        new(d => d.HasBreachNotification, 1, "Contract lacks breach notification language.", "breach notification clause"),
        new(d => d.HasEncryption, 1, "No evidence of encryption controls for payment data.", "encryption controls"),
        new(d => d.HasRetentionSchedule, 1, "No documented data retention schedule on file.", "data retention schedule"),
    ];

    /// <inheritdoc/>
    public RiskAssessment Evaluate(IReadOnlyList<Document> documents, string? question = null)
    {
        if (documents.Count == 0)
        {
            return new RiskAssessment(
                RiskLevel.High,
                ["No evidence on file for this vendor."],
                [],
                Controls.Select(c => c.MissingEvidenceLabel).ToList());
        }

        var policies = documents.Where(d => d.DocType == "policy").ToList();
        var contracts = documents.Where(d => d.DocType != "policy").ToList();
        var requirementSource = policies.Count > 0 ? policies[0] : documents[0];

        var score = 0;
        var reasons = new List<string>();
        var citations = new List<Citation>();
        var missingEvidence = new List<string>();

        foreach (var control in Controls)
        {
            if (documents.Any(control.IsSatisfiedBy))
            {
                continue;
            }

            score += control.Weight;
            reasons.Add(control.Reason);
            missingEvidence.Add(control.MissingEvidenceLabel);

            // Cite the policy that requires the control, not the document that lacks it —
            // that is what a reviewer needs to see.
            citations.Add(new Citation(requirementSource.DocumentId, Excerpt(requirementSource.Text)));
        }

        var injection = FindInjection(documents);
        if (injection is not null)
        {
            var (document, matched) = injection.Value;
            score += 1;
            reasons.Add(InjectionDetectedReason);
            citations.Add(new Citation(document.DocumentId, Excerpt(document.Text, around: matched)));
        }

        RiskLevel riskLevel;
        if (score >= HighThreshold)
        {
            riskLevel = RiskLevel.High;
        }
        else if (score >= MediumThreshold)
        {
            riskLevel = RiskLevel.Medium;
        }
        else
        {
            riskLevel = RiskLevel.Low;
            reasons.Add("All required security evidence is present for this vendor.");
            var source = contracts.Count > 0 ? contracts[0] : documents[0];
            citations.Add(new Citation(source.DocumentId, Excerpt(source.Text)));
        }

        // Stable, de-duplicated citations (several missing controls cite one policy).
        var seen = new HashSet<(string, string)>();
        var uniqueCitations = new List<Citation>();
        foreach (var citation in citations)
        {
            if (seen.Add((citation.DocumentId, citation.Snippet)))
            {
                uniqueCitations.Add(citation);
            }
        }

        return new RiskAssessment(riskLevel, reasons, uniqueCitations, missingEvidence);
    }

    private static (Document Document, string Matched)? FindInjection(IReadOnlyList<Document> documents)
    {
        foreach (var document in documents)
        {
            foreach (var pattern in InjectionPatterns)
            {
                var match = pattern.Match(document.Text);
                if (match.Success)
                {
                    return (document, match.Value);
                }
            }
        }

        return null;
    }

    /// <summary>A citation-sized excerpt, centred on <paramref name="around"/> when it occurs in the text.</summary>
    /// <remarks>
    /// The centred branch prepends an ellipsis to a window of up to
    /// <see cref="WorkflowResult.SnippetMaxChars"/>, so it can in principle return three
    /// characters over the cap. It does not for the current corpus (the window is bounded by
    /// end-of-text well before the limit), and <see cref="WorkflowResult.Validate"/> would
    /// reject it loudly if a longer document ever changed that. Behaviour is preserved from
    /// the original implementation rather than silently corrected.
    /// </remarks>
    private static string Excerpt(string text, string? around = null)
    {
        if (!string.IsNullOrEmpty(around))
        {
            var index = text.IndexOf(around, StringComparison.Ordinal);
            if (index >= 0)
            {
                var start = Math.Max(0, index - 40);
                var length = Math.Min(WorkflowResult.SnippetMaxChars, text.Length - start);
                var excerpt = text.Substring(start, length);
                return start > 0 ? "..." + excerpt : excerpt;
            }
        }

        if (text.Length <= WorkflowResult.SnippetMaxChars)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, WorkflowResult.SnippetMaxChars - 3), "...");
    }
}
