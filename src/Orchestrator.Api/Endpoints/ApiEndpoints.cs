using Microsoft.AspNetCore.Http.HttpResults;
using Orchestrator.Api.Contracts;
using Orchestrator.Api.Middleware;
using Orchestrator.Core.Actions;
using Orchestrator.Core.Audit;
using Orchestrator.Core.Evidence;
using Orchestrator.Core.Fixtures;
using Orchestrator.Core.Models;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Api.Endpoints;

/// <summary>The HTTP surface.</summary>
public static class ApiEndpoints
{
    /// <summary>Map every route under <c>/api/v1</c>, plus the health probe.</summary>
    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("Health")
            .WithSummary("Liveness probe.");

        var api = app.MapGroup("/api/v1");

        api.MapPost("/workflow/run", RunWorkflow)
            .WithName("RunWorkflow")
            .WithSummary("Assess a vendor question and gate any action it requests.")
            .WithDescription(
                "Identity comes from the X-Tenant-Id, X-User-Id and X-Role headers — never the body. " +
                "A blocked action returns 200 with actionStatus describing the block; the body still " +
                "carries the reasons, citations and audit ids the caller needs.");

        api.MapGet("/audit", ReadAudit)
            .WithName("ReadAudit")
            .WithSummary("Read the calling tenant's audit trail.")
            .WithDescription(
                "Always scoped to X-Tenant-Id. There is no tenant parameter: a caller may only ever " +
                "read its own trail.");

        api.MapGet("/vendors/{vendorId}/status", GetVendorStatus)
            .WithName("GetVendorStatus")
            .WithSummary("Approval state of a vendor, for the calling tenant only.");

        api.MapGet("/evidence", GetEvidence)
            .WithName("GetEvidence")
            .WithSummary("The calling tenant's evidence for the vendor.")
            .WithDescription(
                "Makes the isolation property observable: the same path returns disjoint document " +
                "sets for tenant-a and tenant-b.");
    }

    private static Ok<WorkflowResponse> RunWorkflow(
        RunWorkflowRequest body,
        ICallerContextAccessor accessor,
        IWorkflowEngine engine)
    {
        var caller = Require(accessor);

        if (string.IsNullOrWhiteSpace(body.Question))
        {
            throw new InvalidRequestException("missing required field: question");
        }

        var result = engine.Run(new WorkflowRequest
        {
            // Identity from the principal, never from the body.
            TenantId = caller.TenantId,
            UserId = caller.UserId,
            Role = caller.Role,
            Question = body.Question,
            RequestedAction = body.RequestedAction,
            ApprovedBy = body.ApprovedBy,
        });

        return TypedResults.Ok(WorkflowResponse.From(result));
    }

    private static Ok<IReadOnlyList<AuditEventResponse>> ReadAudit(
        ICallerContextAccessor accessor,
        IAuditLog auditLog)
    {
        var caller = Require(accessor);
        IReadOnlyList<AuditEventResponse> events =
            [.. auditLog.Read(caller.TenantId).Select(AuditEventResponse.From)];
        return TypedResults.Ok(events);
    }

    private static Ok<VendorStatusResponse> GetVendorStatus(
        string vendorId,
        ICallerContextAccessor accessor,
        IVendorStateStore vendorState)
    {
        var caller = Require(accessor);
        return TypedResults.Ok(new VendorStatusResponse(
            caller.TenantId,
            vendorId,
            vendorState.Status(caller.TenantId, vendorId)));
    }

    private static Ok<IReadOnlyList<DocumentResponse>> GetEvidence(
        ICallerContextAccessor accessor,
        IEvidenceStore evidenceStore)
    {
        var caller = Require(accessor);
        IReadOnlyList<DocumentResponse> documents =
            [.. evidenceStore.Search(caller.TenantId, EvidenceFixtures.VendorId)
                .Select(DocumentResponse.From)];
        return TypedResults.Ok(documents);
    }

    private static ICallerContext Require(ICallerContextAccessor accessor) =>
        accessor.Current ?? throw new InvalidRequestException("caller identity was not resolved");
}
