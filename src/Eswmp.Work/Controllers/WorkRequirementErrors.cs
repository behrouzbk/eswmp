using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Eswmp.Work.Controllers;

/// <summary>issues[] entry — path/code/severity/message, per docs/api/specs/02-work-requirement-api.md §2.</summary>
public record RequirementIssueDto(string? Path, string Code, string Severity, string Message);

/// <summary>
/// The error shape every 4xx from the Work Requirement controllers shares — matches the
/// shipped Demand Intake envelope (api spec §7: "the shipped hybrid" is adopted over the
/// spec's own RFC-7807 wording) so the domain presents one coherent surface.
/// </summary>
public record RequirementErrorEnvelope(
    string Error,
    string Code,
    string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RequirementIssueDto>? Issues = null);

/// <summary>Shared 4xx-building and idempotency-hashing helpers for RequirementTemplatesController and WorkRequirementsController.</summary>
internal static class WorkRequirementControllerExtensions
{
    public static ObjectResult ErrorResult(this ControllerBase controller, int statusCode, string code, string message, IReadOnlyList<RequirementIssueDto>? issues = null) =>
        controller.StatusCode(statusCode, new RequirementErrorEnvelope(message, code, controller.HttpContext?.TraceIdentifier ?? "", issues));

    public static ObjectResult NotFoundError(this ControllerBase controller, string message) =>
        controller.ErrorResult(StatusCodes.Status404NotFound, "NOT_FOUND", message);

    public static ObjectResult IdempotencyConflict(this ControllerBase controller, string idempotencyKey) =>
        controller.ErrorResult(StatusCodes.Status409Conflict, "IDEMPOTENCY_CONFLICT",
            $"Idempotency-Key '{idempotencyKey}' was already used with a different request body.");

    public static ObjectResult VersionConflict(this ControllerBase controller, long expectedVersion, long? currentVersion) =>
        controller.ErrorResult(StatusCodes.Status412PreconditionFailed, "VERSION_CONFLICT",
            currentVersion is null
                ? $"Expected version {expectedVersion} no longer matches the current version."
                : $"Expected version {expectedVersion} does not match current version {currentVersion}.");

    public static ObjectResult StatusConflict(this ControllerBase controller, string message) =>
        controller.ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", message);

    public static ObjectResult ValidationFailed(this ControllerBase controller, string message, IReadOnlyList<RequirementIssueDto>? issues = null) =>
        controller.ErrorResult(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", message, issues);

    public static ObjectResult InvalidWorkRequirement(this ControllerBase controller, IReadOnlyList<RequirementIssueDto> issues) =>
        controller.ErrorResult(StatusCodes.Status422UnprocessableEntity, "INVALID_WORK_REQUIREMENT", "The work requirement contains incompatible constraints.", issues);

    public static string HashRequest(object request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public static bool IsUniqueViolation(this Microsoft.EntityFrameworkCore.DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };
}
