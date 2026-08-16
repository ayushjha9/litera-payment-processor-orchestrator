using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Orchestrator.Api.Tests;

/// <summary>
/// Hosts the API in-process for tests.
/// </summary>
/// <remarks>
/// Each factory instance gets its own DI container and therefore its own audit log and
/// vendor state, so test classes stay independent under xUnit's parallel execution.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Question = "Can we approve Vendor X to process customer payment data?";

    /// <summary>Mirrors the server's wire format so assertions read the same names clients see.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>A client carrying the given identity headers.</summary>
    public HttpClient ClientFor(string tenantId, string userId, string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-Role", role);
        return client;
    }

    /// <summary>POST a raw JSON body, bypassing typed serialization so malformed input can be tested.</summary>
    public static Task<HttpResponseMessage> PostRaw(HttpClient client, string json) =>
        client.PostAsync("/api/v1/workflow/run",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    /// <summary>POST a well-formed workflow request.</summary>
    public static Task<HttpResponseMessage> Run(
        HttpClient client, string? question = null, string? requestedAction = null, string? approvedBy = null)
    {
        var body = new Dictionary<string, string?> { ["question"] = question ?? Question };
        if (requestedAction is not null) body["requestedAction"] = requestedAction;
        if (approvedBy is not null) body["approvedBy"] = approvedBy;
        return client.PostAsJsonAsync("/api/v1/workflow/run", body);
    }
}

/// <summary>The response shape, as a client sees it.</summary>
public sealed record WorkflowDto(
    string RiskLevel,
    string Recommendation,
    List<string> Reasons,
    List<CitationDto> Citations,
    List<string> MissingEvidence,
    bool RequiresApproval,
    string ActionStatus,
    List<string> AuditEventIds);

/// <summary>A citation, as a client sees it.</summary>
public sealed record CitationDto(string DocumentId, string Snippet);

/// <summary>An audit event, as a client sees it.</summary>
public sealed record AuditDto(
    string EventId,
    string Timestamp,
    string EventType,
    string TenantId,
    string UserId,
    string Role,
    Dictionary<string, JsonElement> Details);
