using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Ui.Components.Contracts;

/// <summary>
/// The API's responses, as a client sees them.
/// </summary>
/// <remarks>
/// <para>
/// These deliberately duplicate <c>Orchestrator.Api.Contracts</c> rather than referencing it.
/// The UI is an HTTP client and should hold only the wire contract: a project reference to
/// <c>Orchestrator.Core</c> would let a page call the risk evaluator directly or read the
/// evidence fixtures, bypassing the tenant choke point in <c>InMemoryEvidenceStore.Search</c>
/// and making the UI a second place tenant filtering could go wrong. Having no reference makes
/// that impossible rather than merely discouraged.
/// </para>
/// <para>
/// The same reasoning already appears in <c>tests/Orchestrator.Api.Tests/ApiFactory.cs</c>,
/// which declares its own DTOs so assertions read the names a client sees.
/// </para>
/// </remarks>
public sealed record WorkflowResponseDto(
    RiskLevelDto RiskLevel,
    string Recommendation,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<CitationDto> Citations,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresApproval,
    ActionStatusDto ActionStatus,
    IReadOnlyList<string> AuditEventIds);

/// <summary>
/// A quoted excerpt backing a stated reason.
/// </summary>
/// <remarks>
/// <see cref="Snippet"/> is <b>untrusted vendor prose</b>. It is quoted so a reviewer can read
/// what the decision was based on, which means it must render as text and never as markup.
/// See <c>CitationList.razor</c>.
/// </remarks>
public sealed record CitationDto(string DocumentId, string Snippet);

/// <summary>One audit record, scoped to the calling tenant.</summary>
public sealed record AuditEventDto(
    string EventId,
    string Timestamp,
    string EventType,
    string TenantId,
    string UserId,
    string Role,
    IReadOnlyDictionary<string, JsonElement> Details);

/// <summary>Current approval state of a vendor, for the calling tenant only.</summary>
public sealed record VendorStatusDto(string TenantId, string VendorId, string Status);

/// <summary>
/// A tenant-scoped evidence document.
/// </summary>
/// <remarks>
/// <see cref="Text"/> and <see cref="Title"/> are written by the party being assessed. Same
/// rule as <see cref="CitationDto.Snippet"/>: text, never markup.
/// </remarks>
public sealed record DocumentDto(
    string DocumentId,
    string TenantId,
    string VendorId,
    string DocType,
    string Title,
    string Text,
    bool HasSoc2,
    bool HasEncryption,
    bool HasBreachNotification,
    bool HasRetentionSchedule);

/// <summary>Risk level, mirroring the API's enum.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskLevelDto>))]
public enum RiskLevelDto
{
    /// <summary>All required evidence present.</summary>
    Low,

    /// <summary>Gaps worth closing, but the action is not gated.</summary>
    Medium,

    /// <summary>Gated. Only this level requires approval.</summary>
    High,
}

/// <summary>
/// The gate's verdict, mirroring the API's enum.
/// </summary>
/// <remarks>
/// Deserialization throws on a value this enum does not know. That is deliberate: if the API
/// grows a new status, the UI should fail loudly rather than render an empty badge next to a
/// compliance decision, where a reviewer would read the absence as "nothing happened".
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ActionStatusDto>))]
public enum ActionStatusDto
{
    /// <summary>No action was asked for; the run was advisory.</summary>
    NotRequested,

    /// <summary>The action ran.</summary>
    Executed,

    /// <summary>High risk, and no valid approval was recorded.</summary>
    BlockedPendingApproval,

    /// <summary>The requesting role may not execute this action, approval or not.</summary>
    BlockedUnauthorized,

    /// <summary>The action is not on the allow-list.</summary>
    BlockedUnknownAction,
}

/// <summary>The three roles the API accepts in <c>X-Role</c>.</summary>
public enum RoleDto
{
    /// <summary>Read-only. May never execute an action.</summary>
    Viewer,

    /// <summary>May execute a non-gated action.</summary>
    Analyst,

    /// <summary>May execute a gated action against a valid approval.</summary>
    Approver,
}

/// <summary>Shared wire-format helpers.</summary>
public static class WireFormat
{
    /// <summary>
    /// Serializer options matching the API's, so the client reads exactly what the server writes.
    /// </summary>
    /// <remarks>
    /// Property names are camelCase while enum <i>values</i> are snake_case
    /// (<c>blocked_pending_approval</c>). One naming policy cannot produce both, which is why
    /// the converter carries its own. This mirrors
    /// <c>src/Orchestrator.Api/Serialization/JsonConfig.cs</c>.
    /// </remarks>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>The wire spelling of an enum value — <c>blocked_pending_approval</c>.</summary>
    public static string Wire<T>(this T value) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!);

    /// <summary>A human-readable label — <c>Blocked pending approval</c>.</summary>
    public static string Label<T>(this T value) where T : struct, Enum
    {
        var wire = value.Wire().Replace('_', ' ');
        return char.ToUpperInvariant(wire[0]) + wire[1..];
    }
}
