namespace Orchestrator.Core.Models;

/// <summary>
/// A piece of tenant-scoped evidence.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Text"/> is vendor-supplied prose. It is UNTRUSTED: it is only ever read to
/// build citation snippets and to <i>detect</i> (never obey) instruction-like content.
/// </para>
/// <para>
/// Every risk decision is made from the structured <c>Has*</c> flags, which is what a real
/// evidence-intake pipeline would extract and a human would attest to. That split is the
/// trust boundary of this system: there is no code path from <see cref="Text"/> to a risk
/// score.
/// </para>
/// </remarks>
public sealed record Document
{
    /// <summary>Stable identifier. This — never <see cref="Text"/> — is what reaches the audit log.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Owning tenant. Filtering on this happens in exactly one place: the evidence store.</summary>
    public required string TenantId { get; init; }

    public required string VendorId { get; init; }

    /// <summary><c>"policy"</c> or <c>"contract"</c>.</summary>
    public required string DocType { get; init; }

    public required string Title { get; init; }

    /// <summary>Untrusted vendor prose. Quoted and scanned; never interpreted.</summary>
    public required string Text { get; init; }

    public bool HasSoc2 { get; init; }

    public bool HasEncryption { get; init; }

    public bool HasBreachNotification { get; init; }

    public bool HasRetentionSchedule { get; init; }
}
