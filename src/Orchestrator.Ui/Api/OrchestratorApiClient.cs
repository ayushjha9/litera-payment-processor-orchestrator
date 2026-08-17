using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Orchestrator.Ui.Components.Contracts;
using Orchestrator.Ui.Components.Status;

namespace Orchestrator.Ui.Api;

/// <summary>A request to run the workflow.</summary>
/// <remarks>
/// Mirrors the API's contract, which declares no identity fields. Adding one here would be
/// rejected with a <c>400</c> by <c>JsonUnmappedMemberHandling.Disallow</c> — the control that
/// stops a caller asserting its own role. <c>ApprovedBy</c> belongs in the body because it
/// describes a third party rather than the caller.
/// </remarks>
public sealed record RunWorkflowRequestDto(string Question, string? RequestedAction, string? ApprovedBy);

/// <summary>Typed client for the orchestrator API.</summary>
public sealed class OrchestratorApiClient(HttpClient http, ILogger<OrchestratorApiClient> logger)
{
    /// <summary>Assess a vendor question and gate any action it requests.</summary>
    public async Task<WorkflowResponseDto> RunWorkflowAsync(
        RunWorkflowRequestDto request, CancellationToken cancellationToken = default)
    {
        // Null members are omitted, so an unrequested action sends no field at all rather than
        // an explicit null. Both are accepted by the API; sending nothing is the honest shape.
        var response = await http.PostAsJsonAsync(
            "/api/v1/workflow/run", request, WireFormat.Json, cancellationToken);

        return await ReadAsync<WorkflowResponseDto>(response, cancellationToken);
    }

    /// <summary>Read the calling tenant's audit trail.</summary>
    public async Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("/api/v1/audit", cancellationToken);
        return await ReadAsync<IReadOnlyList<AuditEventDto>>(response, cancellationToken);
    }

    /// <summary>Read the calling tenant's evidence for the vendor.</summary>
    public async Task<IReadOnlyList<DocumentDto>> GetEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("/api/v1/evidence", cancellationToken);
        return await ReadAsync<IReadOnlyList<DocumentDto>>(response, cancellationToken);
    }

    /// <summary>
    /// Probe the API's readiness.
    /// </summary>
    /// <remarks>
    /// Never throws. This is the one call whose failure is a normal, expected answer — "the API
    /// is unavailable" is information, not an error condition — so it reports a status instead.
    /// </remarks>
    public async Task<ApiStatusBadge.ApiStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await http.GetAsync("/health/ready", cancellationToken);
            return response.IsSuccessStatusCode
                ? ApiStatusBadge.ApiStatus.Ready
                : ApiStatusBadge.ApiStatus.NotReady;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return ApiStatusBadge.ApiStatus.Unreachable;
        }
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(WireFormat.Json, cancellationToken)
                   ?? throw new ApiProblemException(response.StatusCode, "Empty response",
                       "The API returned no body.");
        }

        throw await ToProblemAsync(response, cancellationToken);
    }

    /// <summary>
    /// Turn a failure response into an exception carrying the API's own wording.
    /// </summary>
    /// <remarks>
    /// The API's message is passed through unaltered and never reinterpreted. This matters most
    /// for the <c>403</c>: it says "The requesting tenant is not permitted to run this
    /// workflow" and deliberately does <i>not</i> name the rejected tenant, because a
    /// fail-closed response that identifies it is a tenant-enumeration oracle. The UI knows
    /// which tenant it asked about and could helpfully substitute it — which would rebuild the
    /// oracle at the last step, in the one place nobody would think to look for it.
    /// </remarks>
    private async Task<ApiProblemException> ToProblemAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Not a ProblemDetails body. Fall through to the status code alone.
        }

        logger.LogWarning(
            "API returned {StatusCode} for {Path}: {Title}",
            (int)response.StatusCode, response.RequestMessage?.RequestUri?.AbsolutePath, problem?.Title);

        return new ApiProblemException(
            response.StatusCode,
            problem?.Title ?? response.ReasonPhrase ?? "Request failed",
            problem?.Detail ?? "The API rejected the request.");
    }
}

/// <summary>A non-success response from the API, carrying the API's own wording.</summary>
public sealed class ApiProblemException(HttpStatusCode statusCode, string title, string detail)
    : Exception($"{(int)statusCode} {title}: {detail}")
{
    /// <summary>The status the API returned.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The ProblemDetails title, as the API worded it.</summary>
    public string Title { get; } = title;

    /// <summary>The ProblemDetails detail, as the API worded it.</summary>
    public string Detail { get; } = detail;
}
