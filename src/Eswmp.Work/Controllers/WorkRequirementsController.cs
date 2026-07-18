using System.Text.Json;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using Eswmp.Work.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Controllers;

public record ResolveWorkRequirementRequest(string SourceType, string SourceId, int? SourceVersion, string TemplateCode, JsonElement? Inputs);
public record ReviseWorkRequirementRequest(int ExpectedVersion, string? Reason, JsonElement? Changes);

/// <summary>
/// docs/api/specs/02-work-requirement-api.md §5. The core operation is `resolve`: translate a
/// Demand plus a Template into an operational requirement — the canonical contract every
/// downstream service (Eligibility, Matching, Capacity, Scheduling) reads.
/// </summary>
[ApiController]
[Route("api/v1/work-requirements")]
public class WorkRequirementsController(
    WorkDbContext db,
    ITenantContext tenantContext,
    IOutboxPublisher outbox) : ControllerBase
{
    private static readonly WorkRequirementStatus[] TerminalStatuses = [WorkRequirementStatus.Cancelled, WorkRequirementStatus.Completed];

    [HttpPost("resolve")]
    [RequirePermission(EswmpPermissions.WorkRequirementResolve)]
    public async Task<IActionResult> Resolve(
        ResolveWorkRequirementRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return this.ValidationFailed("The Idempotency-Key header is required.");
        }

        var tenantId = tenantContext.RequiredTenantId;
        var requestHash = WorkRequirementControllerExtensions.HashRequest(request);

        var existing = await db.WorkRequirementIdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "resolve");
        if (existing is not null)
        {
            return existing.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : StatusCode(StatusCodes.Status201Created, JsonSerializer.Deserialize<JsonElement>(existing.ResponseBodyJson));
        }

        if (request.Inputs is { } inputsElement)
        {
            try
            {
                JsonGuard.Validate(inputsElement.GetRawText(), "inputs");
            }
            catch (JsonBoundsExceededException ex)
            {
                return this.ErrorResult(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", ex.Message);
            }
        }

        var template = await db.RequirementTemplates.FirstOrDefaultAsync(t => t.Code == request.TemplateCode);
        var activeVersion = template is null ? null : await db.RequirementTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == template.Id && v.Status == TemplateVersionStatus.Active);
        if (template is null || activeVersion is null)
        {
            return this.ErrorResult(StatusCodes.Status409Conflict, "TEMPLATE_NOT_ACTIVE",
                $"Template '{request.TemplateCode}' has no Active version.");
        }

        var definitions = JsonSerializer.Deserialize<RequirementSetDto>(activeVersion.DefinitionJson, RequirementResolutionService.JsonOptions);
        if (definitions is null)
        {
            return this.ErrorResult(StatusCodes.Status409Conflict, "TEMPLATE_NOT_ACTIVE",
                $"Template '{request.TemplateCode}' version {activeVersion.Version} has no requirement definitions configured.");
        }

        var wr = new WorkRequirement
        {
            TenantId = tenantId,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SourceVersion = request.SourceVersion,
            TemplateId = template.Id,
            TemplateVersion = activeVersion.Version,
            WorkType = template.WorkType,
            Status = WorkRequirementStatus.Draft,
            RequirementVersion = 1,
        };
        RequirementResolutionService.ApplyDefinitions(wr, definitions, tenantId);
        RequirementResolutionService.ApplyInputs(wr, request.Inputs, tenantId);

        var issues = RequirementValidationService.Evaluate(wr);
        var errors = issues.Where(i => i.Severity == "Error")
            .Select(i => new RequirementIssueDto(i.Path, i.Code, i.Severity, i.Message)).ToList();
        if (errors.Count > 0)
        {
            return this.InvalidWorkRequirement(errors);
        }

        var warnings = issues.Where(i => i.Severity == "Warning").Select(i => i.Message).ToList();
        wr.Status = WorkRequirementStatus.Valid;

        db.WorkRequirements.Add(wr);
        db.RequirementVersions.Add(new RequirementVersion
        {
            TenantId = tenantId,
            WorkRequirementId = wr.Id,
            Version = 1,
            ChangeType = "Initial",
            SourceVersion = wr.SourceVersion,
            TemplateVersion = wr.TemplateVersion,
            SnapshotJson = JsonSerializer.Serialize(RequirementResolutionService.ToRequirementSet(wr), RequirementResolutionService.JsonOptions),
        });

        outbox.Enqueue(db, tenantId, "WorkRequirementCreated", "WorkRequirement", wr.Id,
            new WorkRequirementCreatedEvent(wr.Id, tenantId, wr.SourceType, wr.SourceId, Guid.NewGuid()));
        outbox.Enqueue(db, tenantId, "WorkRequirementResolved", "WorkRequirement", wr.Id,
            new WorkRequirementResolvedEvent(wr.Id, tenantId, wr.SourceType, wr.SourceId, wr.RequirementVersion, wr.WorkType,
                ["Eligibility", "Capacity", "Scheduling"], Guid.NewGuid()));

        var responseBody = new
        {
            workRequirementId = wr.Id,
            requirementVersion = wr.RequirementVersion,
            templateCode = template.Code,
            templateVersion = activeVersion.Version,
            status = wr.Status.ToString(),
            warnings,
        };

        db.WorkRequirementIdempotencyRecords.Add(new WorkRequirementIdempotencyRecord
        {
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Operation = "resolve",
            ResourceId = wr.Id,
            ResponseBodyJson = JsonSerializer.Serialize(responseBody, RequirementResolutionService.JsonOptions),
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            var winner = await db.WorkRequirementIdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "resolve");
            if (winner is null)
            {
                throw;
            }
            return winner.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : StatusCode(StatusCodes.Status201Created, JsonSerializer.Deserialize<JsonElement>(winner.ResponseBodyJson));
        }

        return StatusCode(StatusCodes.Status201Created, responseBody);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wr = await db.WorkRequirements.FirstOrDefaultAsync(w => w.Id == id);
        if (wr is null)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        return Ok(await ToWorkRequirementDto(wr));
    }

    [HttpGet("{id:guid}/versions/{version:int}")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> GetVersion(Guid id, int version)
    {
        var exists = await db.WorkRequirements.AnyAsync(w => w.Id == id);
        if (!exists)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        var snapshot = await db.RequirementVersions.FirstOrDefaultAsync(v => v.WorkRequirementId == id && v.Version == version);
        if (snapshot is null)
        {
            return this.NotFoundError($"No version {version} was found for work requirement '{id}'.");
        }

        return Ok(new
        {
            workRequirementId = id,
            requirementVersion = snapshot.Version,
            changeType = snapshot.ChangeType,
            changeReason = snapshot.ChangeReason,
            sourceVersion = snapshot.SourceVersion,
            templateVersion = snapshot.TemplateVersion,
            createdAt = snapshot.CreatedAt,
            resolved = JsonSerializer.Deserialize<JsonElement>(snapshot.SnapshotJson),
        });
    }

    [HttpGet("{id:guid}/resolved")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> GetResolved(Guid id)
    {
        var wr = await LoadFull(id);
        if (wr is null)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        if (wr.Status == WorkRequirementStatus.Invalid)
        {
            return this.StatusConflict("This work requirement is Invalid and cannot be read as a resolved contract.");
        }

        var set = RequirementResolutionService.ToRequirementSet(wr);
        return Ok(new
        {
            workRequirementId = wr.Id,
            requirementVersion = wr.RequirementVersion,
            workType = wr.WorkType,
            set.ResourceRequirements,
            set.CapabilityRequirements,
            set.CertificationRequirements,
            set.CapacityRequirements,
            set.DurationRequirement,
            set.TimeRequirement,
            set.LocationRequirement,
            set.ExecutionRequirement,
            set.TravelRequirement,
            set.BufferRequirements,
            set.DependencyRequirements,
            set.Constraints,
            set.Preferences,
        });
    }

    [HttpPost("{id:guid}/validate")]
    [RequirePermission(EswmpPermissions.WorkRequirementValidate)]
    public async Task<IActionResult> Validate(Guid id)
    {
        var wr = await LoadFull(id);
        if (wr is null)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        var issues = RequirementValidationService.Evaluate(wr);
        var errors = issues.Where(i => i.Severity == "Error").Select(i => new RequirementIssueDto(i.Path, i.Code, i.Severity, i.Message)).ToList();
        var warnings = issues.Where(i => i.Severity == "Warning").Select(i => new RequirementIssueDto(i.Path, i.Code, i.Severity, i.Message)).ToList();

        wr.Status = errors.Count == 0 ? WorkRequirementStatus.Valid : WorkRequirementStatus.Invalid;

        outbox.Enqueue(db, wr.TenantId, errors.Count == 0 ? "WorkRequirementValidated" : "WorkRequirementInvalidated", "WorkRequirement", wr.Id,
            errors.Count == 0
                ? new WorkRequirementValidatedEvent(wr.Id, wr.TenantId, wr.RequirementVersion, Guid.NewGuid())
                : new WorkRequirementInvalidatedEvent(wr.Id, wr.TenantId, wr.RequirementVersion, errors.Select(e => e.Code).ToList(), Guid.NewGuid()));

        await db.SaveChangesAsync();

        return Ok(new { valid = errors.Count == 0, errors, warnings });
    }

    [HttpPost("{id:guid}/revisions")]
    [RequirePermission(EswmpPermissions.WorkRequirementRevise)]
    public async Task<IActionResult> Revise(
        Guid id,
        ReviseWorkRequirementRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return this.ValidationFailed("The Idempotency-Key header is required.");
        }

        var tenantId = tenantContext.RequiredTenantId;
        var requestHash = WorkRequirementControllerExtensions.HashRequest(request);

        var existing = await db.WorkRequirementIdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "revise");
        if (existing is not null)
        {
            return existing.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : StatusCode(StatusCodes.Status201Created, JsonSerializer.Deserialize<JsonElement>(existing.ResponseBodyJson));
        }

        var wr = await LoadFull(id);
        if (wr is null)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        if (request.ExpectedVersion != wr.RequirementVersion)
        {
            return this.VersionConflict(request.ExpectedVersion, wr.RequirementVersion);
        }

        if (TerminalStatuses.Contains(wr.Status))
        {
            return this.StatusConflict($"Work requirement is {wr.Status} and can no longer be revised.");
        }

        var changedCategories = new List<string>();
        if (request.Changes is { ValueKind: JsonValueKind.Object } changesElement)
        {
            try
            {
                JsonGuard.Validate(changesElement.GetRawText(), "changes");
            }
            catch (JsonBoundsExceededException ex)
            {
                return this.ErrorResult(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", ex.Message);
            }

            ApplyRevisionChanges(wr, changesElement, tenantId, changedCategories);
        }

        var issues = RequirementValidationService.Evaluate(wr);
        var errors = issues.Where(i => i.Severity == "Error").Select(i => new RequirementIssueDto(i.Path, i.Code, i.Severity, i.Message)).ToList();
        if (errors.Count > 0)
        {
            return this.InvalidWorkRequirement(errors);
        }

        wr.RequirementVersion++;
        wr.Status = WorkRequirementStatus.Valid;
        wr.UpdatedAt = DateTimeOffset.UtcNow;

        // Material = anything a downstream consumer (Eligibility/Capacity/Scheduling) would
        // need to recalculate for; everything else is a Minor change.
        var materialCategories = new[] { "resourceRequirements", "capacityRequirements", "durationRequirement", "timeRequirement", "locationRequirement" };
        var changeType = changedCategories.Any(materialCategories.Contains) ? "Material" : "Minor";

        db.RequirementVersions.Add(new RequirementVersion
        {
            TenantId = tenantId,
            WorkRequirementId = wr.Id,
            Version = wr.RequirementVersion,
            ChangeType = changeType,
            ChangeReason = request.Reason,
            SourceVersion = wr.SourceVersion,
            TemplateVersion = wr.TemplateVersion,
            SnapshotJson = JsonSerializer.Serialize(RequirementResolutionService.ToRequirementSet(wr), RequirementResolutionService.JsonOptions),
        });

        if (changeType == "Material")
        {
            outbox.Enqueue(db, tenantId, "WorkRequirementChanged", "WorkRequirement", wr.Id,
                new WorkRequirementChangedEvent(
                    wr.Id, tenantId, wr.RequirementVersion, changedCategories,
                    wr.ResourceRequirements.Select(r => r.RoleCode).ToList(),
                    wr.CapacityRequirements.Select(c => c.DimensionCode).Distinct().ToList(),
                    Guid.NewGuid()));
        }

        var responseBody = wr;
        db.WorkRequirementIdempotencyRecords.Add(new WorkRequirementIdempotencyRecord
        {
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Operation = "revise",
            ResourceId = wr.Id,
            ResponseBodyJson = JsonSerializer.Serialize(await ToWorkRequirementDto(wr), RequirementResolutionService.JsonOptions),
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            var winner = await db.WorkRequirementIdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey && r.Operation == "revise");
            if (winner is null)
            {
                throw;
            }
            return winner.RequestHash != requestHash
                ? this.IdempotencyConflict(idempotencyKey)
                : StatusCode(StatusCodes.Status201Created, JsonSerializer.Deserialize<JsonElement>(winner.ResponseBodyJson));
        }

        return StatusCode(StatusCodes.Status201Created, await ToWorkRequirementDto(responseBody));
    }

    [HttpGet("{id:guid}/compare")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> Compare(Guid id, [FromQuery] int fromVersion, [FromQuery] int toVersion)
    {
        var from = await db.RequirementVersions.FirstOrDefaultAsync(v => v.WorkRequirementId == id && v.Version == fromVersion);
        var to = await db.RequirementVersions.FirstOrDefaultAsync(v => v.WorkRequirementId == id && v.Version == toVersion);
        if (from is null || to is null)
        {
            return this.NotFoundError($"Version {(from is null ? fromVersion : toVersion)} was not found for work requirement '{id}'.");
        }

        using var fromDoc = JsonDocument.Parse(from.SnapshotJson);
        using var toDoc = JsonDocument.Parse(to.SnapshotJson);

        var changedCategories = new List<string>();
        var details = new Dictionary<string, object>();
        foreach (var property in toDoc.RootElement.EnumerateObject())
        {
            var beforeText = fromDoc.RootElement.TryGetProperty(property.Name, out var beforeValue) ? beforeValue.GetRawText() : "null";
            var afterText = property.Value.GetRawText();
            if (beforeText != afterText)
            {
                changedCategories.Add(property.Name);
                details[property.Name] = new { before = JsonSerializer.Deserialize<JsonElement>(beforeText), after = JsonSerializer.Deserialize<JsonElement>(afterText) };
            }
        }

        return Ok(new
        {
            workRequirementId = id,
            fromVersion,
            toVersion,
            changedCategories,
            details,
        });
    }

    [HttpGet("{id:guid}/explain")]
    [RequirePermission(EswmpPermissions.WorkRequirementExplain)]
    public async Task<IActionResult> Explain(Guid id)
    {
        var wr = await LoadFull(id);
        if (wr is null)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        var templateLabel = wr.TemplateId is null ? null : await db.RequirementTemplates
            .Where(t => t.Id == wr.TemplateId)
            .Select(t => t.Code)
            .FirstOrDefaultAsync();

        var derived = new List<object>();
        foreach (var capacity in wr.CapacityRequirements)
        {
            derived.Add(new
            {
                requirement = $"{capacity.DimensionCode} = {capacity.Quantity}",
                source = templateLabel is null ? "Resolution input" : $"Template {templateLabel} version {wr.TemplateVersion} (or a resolution/revision input override)",
            });
        }
        foreach (var role in wr.ResourceRequirements)
        {
            derived.Add(new
            {
                requirement = role.RoleCode,
                source = templateLabel is null ? "Resolution input" : $"Template {templateLabel} version {wr.TemplateVersion}",
            });
        }

        var roleSummary = wr.ResourceRequirements.Count switch
        {
            0 => "No resource roles are defined.",
            1 => $"One {wr.ResourceRequirements[0].RoleCode} is required" +
                 (wr.DurationRequirement?.EstimatedDurationMinutes is { } mins ? $" for {mins} minutes" : "") +
                 (wr.LocationRequirement is not null ? $" at the {wr.LocationRequirement.LocationMode}." : "."),
            _ => $"{wr.ResourceRequirements.Count} resource roles are required: {string.Join(", ", wr.ResourceRequirements.Select(r => r.RoleCode))}.",
        };

        return Ok(new { summary = roleSummary, derivedRequirements = derived });
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(EswmpPermissions.WorkRequirementRevise)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var wr = await db.WorkRequirements.FirstOrDefaultAsync(w => w.Id == id);
        if (wr is null)
        {
            return this.NotFoundError($"No work requirement '{id}' was found.");
        }

        if (TerminalStatuses.Contains(wr.Status))
        {
            return this.StatusConflict($"Work requirement is {wr.Status} and can no longer be cancelled.");
        }

        wr.Status = WorkRequirementStatus.Cancelled;
        wr.UpdatedAt = DateTimeOffset.UtcNow;

        outbox.Enqueue(db, wr.TenantId, "WorkRequirementCancelled", "WorkRequirement", wr.Id,
            new WorkRequirementCancelledEvent(wr.Id, wr.TenantId, Guid.NewGuid()));

        await db.SaveChangesAsync();

        return Ok(await ToWorkRequirementDto(wr));
    }

    private Task<WorkRequirement?> LoadFull(Guid id) =>
        db.WorkRequirements
            .Include(w => w.ResourceRequirements)
            .Include(w => w.CapabilityRequirements)
            .Include(w => w.CertificationRequirements)
            .Include(w => w.CapacityRequirements)
            .Include(w => w.DurationRequirement)
            .Include(w => w.TimeRequirement)
            .Include(w => w.LocationRequirement)
            .Include(w => w.ExecutionRequirement)
            .Include(w => w.TravelRequirement)
            .Include(w => w.BufferRequirements)
            .Include(w => w.DependencyRequirements)
            .Include(w => w.Constraints)
            .Include(w => w.Preferences)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id);

    private async Task<object> ToWorkRequirementDto(WorkRequirement wr)
    {
        var templateCode = wr.TemplateId is null ? null : await db.RequirementTemplates
            .Where(t => t.Id == wr.TemplateId).Select(t => t.Code).FirstOrDefaultAsync();

        return new
        {
            id = wr.Id,
            tenantId = wr.TenantId,
            sourceType = wr.SourceType,
            sourceId = wr.SourceId,
            sourceVersion = wr.SourceVersion,
            templateId = wr.TemplateId,
            templateCode,
            templateVersion = wr.TemplateVersion,
            workType = wr.WorkType,
            workCategory = wr.WorkCategory,
            serviceMode = wr.ServiceMode,
            status = wr.Status.ToString(),
            priority = wr.Priority.ToString(),
            effectiveFrom = wr.EffectiveFrom,
            effectiveTo = wr.EffectiveTo,
            requirementVersion = wr.RequirementVersion,
            createdAt = wr.CreatedAt,
            updatedAt = wr.UpdatedAt,
        };
    }

    /// <summary>
    /// A revision's `changes` payload replaces named categories wholesale when present —
    /// simple, predictable PATCH semantics rather than per-field diffing. Single-cardinality
    /// categories (duration/time/location/execution/travel) are replaced outright; list
    /// categories are replaced outright too, except capacityRequirements, which is merged by
    /// (roleCode, dimensionCode) so the documented "bump PET_COUNT to 2" example doesn't
    /// require restating every other capacity dimension.
    /// </summary>
    private static void ApplyRevisionChanges(WorkRequirement wr, JsonElement changes, Guid tenantId, List<string> changedCategories)
    {
        if (changes.TryGetProperty("resourceRequirements", out var resourceEl))
        {
            var dtos = JsonSerializer.Deserialize<List<ResourceRoleRequirementDto>>(resourceEl.GetRawText(), RequirementResolutionService.JsonOptions) ?? [];
            wr.ResourceRequirements.Clear();
            foreach (var dto in dtos)
            {
                wr.ResourceRequirements.Add(new ResourceRoleRequirement
                {
                    TenantId = tenantId, WorkRequirementId = wr.Id, RoleCode = dto.RoleCode, ResourceCategory = dto.ResourceCategory,
                    MinimumQuantity = dto.MinimumQuantity, MaximumQuantity = dto.MaximumQuantity, Required = dto.Required,
                    SelectionMode = dto.SelectionMode, SameResourceRequired = dto.SameResourceRequired, Sequence = dto.Sequence,
                });
            }
            changedCategories.Add("resourceRequirements");
        }

        if (changes.TryGetProperty("capacityRequirements", out var capacityEl))
        {
            var dtos = JsonSerializer.Deserialize<List<CapacityRequirementDto>>(capacityEl.GetRawText(), RequirementResolutionService.JsonOptions) ?? [];
            foreach (var dto in dtos)
            {
                var roleId = dto.RoleCode is null ? (Guid?)null : wr.ResourceRequirements.FirstOrDefault(r => r.RoleCode == dto.RoleCode)?.Id;
                var match = wr.CapacityRequirements.FirstOrDefault(c => c.DimensionCode == dto.DimensionCode && c.ResourceRoleRequirementId == roleId);
                if (match is not null)
                {
                    match.Quantity = dto.Quantity;
                    if (dto.Unit is not null) match.Unit = dto.Unit;
                }
                else
                {
                    wr.CapacityRequirements.Add(new CapacityRequirement
                    {
                        TenantId = tenantId, WorkRequirementId = wr.Id, ResourceRoleRequirementId = roleId,
                        DimensionCode = dto.DimensionCode, Quantity = dto.Quantity, Unit = dto.Unit,
                        AggregationScope = dto.AggregationScope, Mandatory = dto.Mandatory,
                    });
                }
            }
            changedCategories.Add("capacityRequirements");
        }

        if (changes.TryGetProperty("durationRequirement", out var durationEl))
        {
            var dto = JsonSerializer.Deserialize<DurationRequirementDto>(durationEl.GetRawText(), RequirementResolutionService.JsonOptions);
            if (dto is not null)
            {
                wr.DurationRequirement ??= new DurationRequirement { TenantId = tenantId, WorkRequirementId = wr.Id, DurationType = dto.DurationType };
                wr.DurationRequirement.DurationType = dto.DurationType;
                wr.DurationRequirement.EstimatedDurationMinutes = dto.EstimatedDurationMinutes;
                wr.DurationRequirement.MinimumDurationMinutes = dto.MinimumDurationMinutes;
                wr.DurationRequirement.MaximumDurationMinutes = dto.MaximumDurationMinutes;
                wr.DurationRequirement.SetupDurationMinutes = dto.SetupDurationMinutes;
                wr.DurationRequirement.CleanupDurationMinutes = dto.CleanupDurationMinutes;
                changedCategories.Add("durationRequirement");
            }
        }

        if (changes.TryGetProperty("timeRequirement", out var timeEl))
        {
            var dto = JsonSerializer.Deserialize<TimeRequirementDto>(timeEl.GetRawText(), RequirementResolutionService.JsonOptions);
            if (dto is not null)
            {
                wr.TimeRequirement ??= new TimeRequirement { TenantId = tenantId, WorkRequirementId = wr.Id, TimeConstraintType = dto.TimeConstraintType };
                wr.TimeRequirement.TimeConstraintType = dto.TimeConstraintType;
                wr.TimeRequirement.EarliestStart = RequirementResolutionService.ToUtc(dto.EarliestStart);
                wr.TimeRequirement.LatestStart = RequirementResolutionService.ToUtc(dto.LatestStart);
                wr.TimeRequirement.EarliestFinish = RequirementResolutionService.ToUtc(dto.EarliestFinish);
                wr.TimeRequirement.LatestFinish = RequirementResolutionService.ToUtc(dto.LatestFinish);
                wr.TimeRequirement.FixedStart = RequirementResolutionService.ToUtc(dto.FixedStart);
                wr.TimeRequirement.FixedEnd = RequirementResolutionService.ToUtc(dto.FixedEnd);
                wr.TimeRequirement.Deadline = RequirementResolutionService.ToUtc(dto.Deadline);
                wr.TimeRequirement.Timezone = dto.Timezone;
                changedCategories.Add("timeRequirement");
            }
        }

        if (changes.TryGetProperty("locationRequirement", out var locationEl))
        {
            var dto = JsonSerializer.Deserialize<LocationRequirementDto>(locationEl.GetRawText(), RequirementResolutionService.JsonOptions);
            if (dto is not null)
            {
                wr.LocationRequirement ??= new LocationRequirement { TenantId = tenantId, WorkRequirementId = wr.Id, LocationMode = dto.LocationMode };
                wr.LocationRequirement.LocationMode = dto.LocationMode;
                wr.LocationRequirement.LocationReferenceType = dto.LocationReferenceType;
                wr.LocationRequirement.LocationReferenceId = dto.LocationReferenceId;
                wr.LocationRequirement.Latitude = dto.Latitude;
                wr.LocationRequirement.Longitude = dto.Longitude;
                wr.LocationRequirement.ServiceRadius = dto.ServiceRadius;
                wr.LocationRequirement.LocationFlexibility = dto.LocationFlexibility;
                changedCategories.Add("locationRequirement");
            }
        }
    }
}
