using System.Text.Json;
using Eswmp.Shared.Auth;
using Eswmp.Shared.DTOs;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using Eswmp.Work.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Controllers;

public record CreateTemplateRequest(string Code, string Name, string? Description, string WorkType);
public record CreateTemplateVersionRequest(string? ChangeReason);
public record TemplateSearchRequest(string? WorkType, TemplateStatus? Status, int Page = 1, int PageSize = 20);

/// <summary>
/// docs/api/specs/02-work-requirement-api.md §4. Templates hold reusable operational
/// defaults; a version is immutable once activated — a change is a new version, never an
/// edit in place (api §1: "Templates are immutable after activation").
/// </summary>
[ApiController]
[Route("api/v1/work-requirement-templates")]
public class RequirementTemplatesController(
    WorkDbContext db,
    ITenantContext tenantContext,
    IOutboxPublisher outbox) : ControllerBase
{
    [HttpPost]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateCreate)]
    public async Task<IActionResult> Create(
        CreateTemplateRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return this.ValidationFailed("The Idempotency-Key header is required.");
        }

        var tenantId = tenantContext.RequiredTenantId;
        var requestHash = WorkRequirementControllerExtensions.HashRequest(request);

        var existing = await db.WorkRequirementIdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "template.create");
        if (existing is not null)
        {
            return existing.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : CreatedAtAction(nameof(GetById), new { id = existing.ResourceId }, JsonSerializer.Deserialize<RequirementTemplate>(existing.ResponseBodyJson));
        }

        var codeExists = await db.RequirementTemplates.AnyAsync(t => t.Code == request.Code);
        if (codeExists)
        {
            return this.ErrorResult(StatusCodes.Status409Conflict, "STATUS_CONFLICT", $"Code '{request.Code}' is already in use for this tenant.");
        }

        var template = new RequirementTemplate
        {
            TenantId = tenantId,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            WorkType = request.WorkType,
            Status = TemplateStatus.Draft,
            CurrentVersion = 1,
        };
        var firstVersion = new RequirementTemplateVersion
        {
            TenantId = tenantId,
            TemplateId = template.Id,
            Version = 1,
            Status = TemplateVersionStatus.Draft,
        };
        template.Versions.Add(firstVersion);

        db.RequirementTemplates.Add(template);
        db.WorkRequirementIdempotencyRecords.Add(new WorkRequirementIdempotencyRecord
        {
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Operation = "template.create",
            ResourceId = template.Id,
            ResponseBodyJson = JsonSerializer.Serialize(template),
        });
        outbox.Enqueue(db, tenantId, "RequirementTemplateCreated", "RequirementTemplate", template.Id,
            new RequirementTemplateCreatedEvent(template.Id, tenantId, template.Code, Guid.NewGuid()));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            var winner = await db.WorkRequirementIdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "template.create");
            if (winner is null)
            {
                throw;
            }
            return winner.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : CreatedAtAction(nameof(GetById), new { id = winner.ResourceId }, JsonSerializer.Deserialize<RequirementTemplate>(winner.ResponseBodyJson));
        }

        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var template = await db.RequirementTemplates.Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == id);
        return template is null ? this.NotFoundError($"No template '{id}' was found.") : Ok(template);
    }

    [HttpPost("search")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateRead)]
    public async Task<IActionResult> Search(TemplateSearchRequest request)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var query = db.RequirementTemplates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.WorkType))
            query = query.Where(t => t.WorkType == request.WorkType);
        if (request.Status is not null)
            query = query.Where(t => t.Status == request.Status);

        var totalCount = await query.CountAsync();
        var items = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new PagedResult<RequirementTemplate> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount });
    }

    [HttpPost("{templateId:guid}/versions")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateUpdate)]
    public async Task<IActionResult> CreateVersion(
        Guid templateId,
        CreateTemplateVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return this.ValidationFailed("The Idempotency-Key header is required.");
        }

        var tenantId = tenantContext.RequiredTenantId;
        var requestHash = WorkRequirementControllerExtensions.HashRequest(request);

        var existing = await db.WorkRequirementIdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "template.version.create");
        if (existing is not null)
        {
            return existing.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : CreatedAtAction(nameof(GetVersion), RouteFor(existing), JsonSerializer.Deserialize<RequirementTemplateVersion>(existing.ResponseBodyJson));
        }

        var template = await db.RequirementTemplates.FirstOrDefaultAsync(t => t.Id == templateId);
        if (template is null)
        {
            return this.NotFoundError($"No template '{templateId}' was found.");
        }

        var version = new RequirementTemplateVersion
        {
            TenantId = tenantId,
            TemplateId = template.Id,
            Version = template.CurrentVersion + 1,
            Status = TemplateVersionStatus.Draft,
            ChangeReason = request.ChangeReason,
        };
        template.CurrentVersion = version.Version;

        db.RequirementTemplateVersions.Add(version);
        db.WorkRequirementIdempotencyRecords.Add(new WorkRequirementIdempotencyRecord
        {
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Operation = "template.version.create",
            ResourceId = version.Id,
            ResponseBodyJson = JsonSerializer.Serialize(version),
        });
        outbox.Enqueue(db, tenantId, "RequirementTemplateVersionCreated", "RequirementTemplate", template.Id,
            new RequirementTemplateVersionCreatedEvent(template.Id, version.Version, tenantId, Guid.NewGuid()));

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetVersion), new { templateId, version = version.Version }, version);
    }

    private static object RouteFor(WorkRequirementIdempotencyRecord record) =>
        new { templateId = record.ResourceId };

    [HttpGet("{templateId:guid}/versions/{version:int}")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateRead)]
    public async Task<IActionResult> GetVersion(Guid templateId, int version)
    {
        var templateVersion = await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.Version == version);
        return templateVersion is null ? this.NotFoundError($"No version {version} was found for template '{templateId}'.") : Ok(templateVersion);
    }

    [HttpPut("{templateId:guid}/versions/{version:int}/requirements")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateUpdate)]
    public async Task<IActionResult> ConfigureRequirements(
        Guid templateId,
        int version,
        [FromBody] JsonElement body,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        // v2 delta (UX-08): two authors editing the same draft version must not silently
        // overwrite each other — If-Match against RowVersion is required, not optional.
        // Accept both a bare value ("1") and a properly RFC 7232-quoted ETag ("\"1\"") —
        // real HTTP clients send the latter.
        if (string.IsNullOrWhiteSpace(ifMatch) || !long.TryParse(ifMatch.Trim('"'), out var expectedRowVersion))
        {
            return this.ValidationFailed("The If-Match header is required and must be the version's current RowVersion.");
        }

        var templateVersion = await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.Version == version);
        if (templateVersion is null)
        {
            return this.NotFoundError($"No version {version} was found for template '{templateId}'.");
        }

        if (expectedRowVersion != templateVersion.RowVersion)
        {
            return this.VersionConflict(expectedRowVersion, templateVersion.RowVersion);
        }

        if (templateVersion.Status != TemplateVersionStatus.Draft)
        {
            return this.ErrorResult(StatusCodes.Status409Conflict, "TEMPLATE_IMMUTABLE", $"Version {version} is {templateVersion.Status} and can no longer be edited.");
        }

        var bodyJson = body.GetRawText();
        try
        {
            JsonGuard.Validate(bodyJson, "requirements");
        }
        catch (JsonBoundsExceededException ex)
        {
            return this.ErrorResult(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", ex.Message);
        }

        RequirementSetDto definitions;
        try
        {
            definitions = JsonSerializer.Deserialize<RequirementSetDto>(bodyJson, RequirementResolutionService.JsonOptions)
                ?? throw new JsonException("Empty body.");
        }
        catch (JsonException ex)
        {
            return this.ValidationFailed($"The request body could not be parsed: {ex.Message}");
        }

        var errors = ValidateDefinitions(definitions)
            .Where(i => i.Severity == "Error")
            .Select(i => new RequirementIssueDto(i.Path, i.Code, i.Severity, i.Message))
            .ToList();
        if (errors.Count > 0)
        {
            return this.InvalidWorkRequirement(errors);
        }

        templateVersion.DefinitionJson = bodyJson;
        templateVersion.RowVersion++;
        await db.SaveChangesAsync();

        return Ok(templateVersion);
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/activate")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateActivate)]
    public async Task<IActionResult> Activate(Guid templateId, int version)
    {
        var template = await db.RequirementTemplates.FirstOrDefaultAsync(t => t.Id == templateId);
        if (template is null)
        {
            return this.NotFoundError($"No template '{templateId}' was found.");
        }

        var templateVersion = await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.Version == version);
        if (templateVersion is null)
        {
            return this.NotFoundError($"No version {version} was found for template '{templateId}'.");
        }

        if (templateVersion.Status != TemplateVersionStatus.Draft)
        {
            return this.StatusConflict($"Version {version} is {templateVersion.Status}; only a Draft version may be activated.");
        }

        RequirementSetDto definitions;
        try
        {
            definitions = JsonSerializer.Deserialize<RequirementSetDto>(templateVersion.DefinitionJson, RequirementResolutionService.JsonOptions)
                ?? throw new JsonException("Empty definitions.");
        }
        catch (JsonException)
        {
            return this.InvalidWorkRequirement([new RequirementIssueDto(null, "MISSING_DEFINITIONS", "Error", "This version has no requirement definitions configured.")]);
        }

        var errors = ValidateDefinitions(definitions)
            .Where(i => i.Severity == "Error")
            .Select(i => new RequirementIssueDto(i.Path, i.Code, i.Severity, i.Message))
            .ToList();
        if (errors.Count > 0)
        {
            return this.InvalidWorkRequirement(errors);
        }

        var priorActive = await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.Status == TemplateVersionStatus.Active);
        if (priorActive is not null)
        {
            priorActive.Status = TemplateVersionStatus.Superseded;
        }

        templateVersion.Status = TemplateVersionStatus.Active;
        template.Status = TemplateStatus.Active;

        outbox.Enqueue(db, template.TenantId, "RequirementTemplateVersionActivated", "RequirementTemplate", template.Id,
            new RequirementTemplateVersionActivatedEvent(template.Id, version, template.TenantId, Guid.NewGuid()));

        await db.SaveChangesAsync();

        return Ok(templateVersion);
    }

    [HttpPost("{templateId:guid}/retire")]
    [RequirePermission(EswmpPermissions.WorkRequirementTemplateRetire)]
    public async Task<IActionResult> Retire(Guid templateId)
    {
        var template = await db.RequirementTemplates.FirstOrDefaultAsync(t => t.Id == templateId);
        if (template is null)
        {
            return this.NotFoundError($"No template '{templateId}' was found.");
        }

        if (template.Status == TemplateStatus.Retired)
        {
            return this.StatusConflict("Template is already Retired.");
        }

        template.Status = TemplateStatus.Retired;

        var activeVersion = await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.Status == TemplateVersionStatus.Active);
        if (activeVersion is not null)
        {
            activeVersion.Status = TemplateVersionStatus.Retired;
        }

        outbox.Enqueue(db, template.TenantId, "RequirementTemplateRetired", "RequirementTemplate", template.Id,
            new RequirementTemplateRetiredEvent(template.Id, template.TenantId, Guid.NewGuid()));

        await db.SaveChangesAsync();

        return Ok(template);
    }

    /// <summary>
    /// Validates a template's definitions by materializing them into a scratch (never
    /// persisted) WorkRequirement and running the same seven-category validator that
    /// resolve/validate/revise use — one rule set for the whole domain.
    /// </summary>
    internal static List<ValidationIssue> ValidateDefinitions(RequirementSetDto definitions)
    {
        var scratch = new WorkRequirement
        {
            TenantId = Guid.Empty,
            SourceType = "Template",
            SourceId = "scratch",
            WorkType = "scratch",
        };
        RequirementResolutionService.ApplyDefinitions(scratch, definitions, Guid.Empty);

        // Structural/source checks don't apply to a scratch template preview — only the
        // requirement-shape rules (semantic/temporal/composition/cross-requirement) do.
        return RequirementValidationService.Evaluate(scratch)
            .Where(i => i.Code is not ("WORK_TYPE_REQUIRED" or "SOURCE_REFERENCE_REQUIRED"))
            .ToList();
    }
}
