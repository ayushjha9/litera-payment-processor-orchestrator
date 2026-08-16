using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orchestrator.Core.Models;

namespace Orchestrator.Api.ErrorHandling;

/// <summary>
/// Maps domain failures onto RFC 7807 responses.
/// </summary>
/// <remarks>
/// The mapping is a security surface in its own right, so each case is deliberate about how
/// much it says. See the individual branches.
/// </remarks>
public sealed class ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            // Fail closed — but say nothing about which tenants exist. Echoing the rejected
            // tenant id back would turn this into a tenant-enumeration oracle.
            UnknownTenantException => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "The requesting tenant is not permitted to run this workflow.",
            },

            InvalidRequestException e => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = e.Message,
            },

            // Unknown or malformed body fields. Minimal APIs wrap the underlying
            // JsonException — thrown by JsonUnmappedMemberHandling.Disallow when a caller
            // sends a field the contract does not declare — in a BadHttpRequestException, so
            // the inner exception is where the useful message lives. Reflecting it is safe:
            // it names the caller's own input.
            BadHttpRequestException e => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request body",
                Detail = (e.InnerException as JsonException)?.Message ?? e.Message,
            },

            JsonException e => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request body",
                Detail = e.Message,
            },

            // A should-never-happen invariant breach: a control failed. Opaque to the caller,
            // logged in full, and per PRODUCTION_NOTES.md this deserves an alert, not a line
            // in a file nobody reads.
            OutputContractException => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal error",
                Detail = "The request could not be completed.",
            },

            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        if (exception is OutputContractException)
        {
            logger.LogCritical(
                exception,
                "Output contract violated — a safety invariant did not hold. This is a should-never-happen condition.");
        }
        else
        {
            logger.LogWarning("Request rejected: {Message}", exception.Message);
        }

        problem.Instance = context.Request.Path;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);
        return true;
    }
}
