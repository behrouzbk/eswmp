using System.Text.Json;
using Eswmp.Shared.Auth;
using Eswmp.Shared.DTOs;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using Eswmp.Work.Data;
using Eswmp.Work.Models;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Work.Controllers;

/// <summary>
/// The first-generation Work Requirement API — see the provenance note on
/// <see cref="RequirementDefinition"/>. Kept alongside the reconciled
/// /api/v1/work-requirements surface (<see cref="WorkRequirementsController"/>) rather than
/// deleted.
/// </summary>
public record CreateRequirementDefinitionRequest(string Code, string Name, string? Description, string? Category);

public record RequirementDefinitionSearchRequest(
    RequirementDefinitionStatus? Status,
    string? Category,
    int Page = 1,
    int PageSize = 20);

public record DefinitionCapabilityRequirementDto(string CapabilityCode, int? MinimumLevel, CapabilityImportance Importance);
public record DefinitionSkillRequirementDto(string SkillCode, int? MinimumLevel, bool Mandatory);
public record DefinitionCertificationRequirementDto(string CertificationTypeCode, bool Mandatory);

public record DefinitionResourceRequirementDto(
    string ResourceTypeCode,
    string? Role,
    int MinimumQuantity,
    int PreferredQuantity,
    int MaximumQuantity,
    bool Mandatory,
    List<DefinitionCapabilityRequirementDto>? Capabilities,
    List<DefinitionSkillRequirementDto>? Skills,
    List<DefinitionCertificationRequirementDto>? Certifications);

public record LocationConstraintDto(
    LocationConstraintMode Mode,
    decimal? MaximumTravelDistanceKm,
    int? MaximumTravelTimeMinutes);

public record RequirementDefinitionVersionRequest(
    string? ChangeSummary,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    DefinitionDurationType DurationType,
    int? FixedDurationMinutes,
    int? MinimumDurationMinutes,
    int? ExpectedDurationMinutes,
    int? MaximumDurationMinutes,
    int PreWorkBufferMinutes,
    int PostWorkBufferMinutes,
    List<DefinitionResourceRequirementDto> ResourceRequirements,
    List<LocationConstraintDto>? LocationConstraints);

public record ActivateDefinitionVersionRequest(int ExpectedVersion);

public record CreateDefinitionSnapshotRequest(int VersionNumber, string? Reason);

[ApiController]
[Route("api/v1/requirement-definitions")]
public class RequirementDefinitionsController(
    WorkDbContext db,
    ITenantContext tenantContext,
    IPublishEndpoint publishEndpoint) : ControllerBase
{
    [HttpPost]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> Create(CreateRequirementDefinitionRequest request)
    {
        var tenantId = tenantContext.RequiredTenantId;

        var codeExists = await db.RequirementDefinitions.AnyAsync(w => w.Code == request.Code);
        if (codeExists)
        {
            return Conflict(new { error = $"Code '{request.Code}' is already in use for this tenant." });
        }

        var definition = new RequirementDefinition
        {
            TenantId = tenantId,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
        };

        db.RequirementDefinitions.Add(definition);
        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new RequirementDefinitionCreatedEvent(
            definition.Id, definition.TenantId, definition.Code, Guid.NewGuid()));

        return CreatedAtAction(nameof(GetById), new { id = definition.Id }, definition);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var definition = await db.RequirementDefinitions
            .Include(w => w.Versions).ThenInclude(v => v.LocationConstraints)
            .Include(w => w.Versions).ThenInclude(v => v.ResourceRequirements).ThenInclude(rr => rr.CapabilityRequirements)
            .Include(w => w.Versions).ThenInclude(v => v.ResourceRequirements).ThenInclude(rr => rr.SkillRequirements)
            .Include(w => w.Versions).ThenInclude(v => v.ResourceRequirements).ThenInclude(rr => rr.CertificationRequirements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id);

        return definition is null ? NotFound() : Ok(definition);
    }

    [HttpPost("search")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionRead)]
    public async Task<IActionResult> Search(RequirementDefinitionSearchRequest request)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var query = db.RequirementDefinitions.AsQueryable();

        if (request.Status is not null)
            query = query.Where(w => w.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(w => w.Category == request.Category);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<RequirementDefinition>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    [HttpPost("{id:guid}/versions")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> CreateVersion(Guid id, RequirementDefinitionVersionRequest request)
    {
        var definition = await db.RequirementDefinitions.FirstOrDefaultAsync(w => w.Id == id);
        if (definition is null)
            return NotFound();

        var versionNumber = definition.CurrentVersionNumber + 1;

        var version = new RequirementDefinitionVersion
        {
            TenantId = definition.TenantId,
            RequirementDefinitionId = definition.Id,
            VersionNumber = versionNumber,
            Status = RequirementDefinitionVersionStatus.Draft,
            ChangeSummary = request.ChangeSummary,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            DurationType = request.DurationType,
            FixedDurationMinutes = request.FixedDurationMinutes,
            MinimumDurationMinutes = request.MinimumDurationMinutes,
            ExpectedDurationMinutes = request.ExpectedDurationMinutes,
            MaximumDurationMinutes = request.MaximumDurationMinutes,
            PreWorkBufferMinutes = request.PreWorkBufferMinutes,
            PostWorkBufferMinutes = request.PostWorkBufferMinutes,
        };

        MapResourceRequirements(version, request.ResourceRequirements);
        MapLocationConstraints(version, request.LocationConstraints);

        db.RequirementDefinitionVersions.Add(version);
        definition.CurrentVersionNumber = versionNumber;

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetVersion), new { id = definition.Id, versionNumber }, version);
    }

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionRead)]
    public async Task<IActionResult> GetVersion(Guid id, int versionNumber)
    {
        var version = await LoadFullVersion(id, versionNumber);
        return version is null ? NotFound() : Ok(version);
    }

    [HttpPatch("{id:guid}/versions/{versionNumber:int}")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> UpdateVersion(Guid id, int versionNumber, RequirementDefinitionVersionRequest request)
    {
        var version = await LoadFullVersion(id, versionNumber);
        if (version is null)
            return NotFound();

        if (version.Status != RequirementDefinitionVersionStatus.Draft)
        {
            return Conflict(new { error = $"Version {versionNumber} is {version.Status}; only Draft versions may be edited." });
        }

        version.ChangeSummary = request.ChangeSummary;
        version.EffectiveFrom = request.EffectiveFrom;
        version.EffectiveTo = request.EffectiveTo;
        version.DurationType = request.DurationType;
        version.FixedDurationMinutes = request.FixedDurationMinutes;
        version.MinimumDurationMinutes = request.MinimumDurationMinutes;
        version.ExpectedDurationMinutes = request.ExpectedDurationMinutes;
        version.MaximumDurationMinutes = request.MaximumDurationMinutes;
        version.PreWorkBufferMinutes = request.PreWorkBufferMinutes;
        version.PostWorkBufferMinutes = request.PostWorkBufferMinutes;

        // Draft edits replace child collections wholesale rather than diffing —
        // simplest correct behavior while a version is still mutable. Grandchildren
        // (capability/skill/certification requirements) are removed explicitly and
        // in order, rather than relying on cascade-delete fixup timing, then the
        // navigation is replaced with a fresh list — Clear() on the tracked
        // navigation would instead try to re-null the FK on an entity already
        // scheduled for deletion.
        foreach (var resourceRequirement in version.ResourceRequirements)
        {
            db.DefinitionCapabilityRequirements.RemoveRange(resourceRequirement.CapabilityRequirements);
            db.DefinitionSkillRequirements.RemoveRange(resourceRequirement.SkillRequirements);
            db.DefinitionCertificationRequirements.RemoveRange(resourceRequirement.CertificationRequirements);
        }

        db.DefinitionResourceRequirements.RemoveRange(version.ResourceRequirements);
        db.LocationConstraints.RemoveRange(version.LocationConstraints);
        version.ResourceRequirements = [];
        version.LocationConstraints = [];

        MapResourceRequirements(version, request.ResourceRequirements);
        MapLocationConstraints(version, request.LocationConstraints);

        await db.SaveChangesAsync();

        return Ok(version);
    }

    [HttpPost("{id:guid}/versions/{versionNumber:int}/validate")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> ValidateVersion(Guid id, int versionNumber)
    {
        var version = await db.RequirementDefinitionVersions
            .Include(v => v.ResourceRequirements)
            .FirstOrDefaultAsync(v => v.RequirementDefinitionId == id && v.VersionNumber == versionNumber);

        if (version is null)
            return NotFound();

        var (status, issues) = RunValidation(version);

        if (status == "Valid" && version.Status == RequirementDefinitionVersionStatus.Draft)
        {
            version.Status = RequirementDefinitionVersionStatus.Validated;
            await db.SaveChangesAsync();
        }

        return Ok(new { Status = status, Issues = issues });
    }

    [HttpPost("{id:guid}/versions/{versionNumber:int}/activate")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> ActivateVersion(Guid id, int versionNumber, ActivateDefinitionVersionRequest request)
    {
        var definition = await db.RequirementDefinitions.FirstOrDefaultAsync(w => w.Id == id);
        if (definition is null)
            return NotFound();

        if (request.ExpectedVersion != definition.ConcurrencyVersion)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, new
            {
                error = $"Expected version {request.ExpectedVersion} does not match current version {definition.ConcurrencyVersion}."
            });
        }

        var version = await db.RequirementDefinitionVersions
            .Include(v => v.ResourceRequirements)
            .FirstOrDefaultAsync(v => v.RequirementDefinitionId == id && v.VersionNumber == versionNumber);

        if (version is null)
            return NotFound();

        if (version.Status != RequirementDefinitionVersionStatus.Draft && version.Status != RequirementDefinitionVersionStatus.Validated)
        {
            return Conflict(new { error = $"Version {versionNumber} is {version.Status}; only Draft or Validated versions may be activated." });
        }

        if (version.Status == RequirementDefinitionVersionStatus.Draft)
        {
            var (status, issues) = RunValidation(version);
            if (status != "Valid")
            {
                return UnprocessableEntity(new { Status = status, Issues = issues });
            }
        }

        if (definition.ActiveVersionNumber is not null)
        {
            var priorActive = await db.RequirementDefinitionVersions.FirstOrDefaultAsync(
                v => v.RequirementDefinitionId == id && v.VersionNumber == definition.ActiveVersionNumber);
            if (priorActive is not null)
            {
                priorActive.Status = RequirementDefinitionVersionStatus.Superseded;
            }
        }

        version.Status = RequirementDefinitionVersionStatus.Active;
        definition.ActiveVersionNumber = versionNumber;
        definition.Status = RequirementDefinitionStatus.Active;
        definition.ConcurrencyVersion++;

        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new RequirementDefinitionVersionActivatedEvent(
            definition.Id, versionNumber, definition.TenantId, Guid.NewGuid()));

        return Ok(version);
    }

    [HttpPost("{id:guid}/retire")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> Retire(Guid id)
    {
        var definition = await db.RequirementDefinitions.FirstOrDefaultAsync(w => w.Id == id);
        if (definition is null)
            return NotFound();

        if (definition.Status == RequirementDefinitionStatus.Retired)
        {
            return Conflict(new { error = "RequirementDefinition is already Retired." });
        }

        definition.Status = RequirementDefinitionStatus.Retired;
        definition.ConcurrencyVersion++;

        if (definition.ActiveVersionNumber is not null)
        {
            var activeVersion = await db.RequirementDefinitionVersions.FirstOrDefaultAsync(
                v => v.RequirementDefinitionId == id && v.VersionNumber == definition.ActiveVersionNumber);
            if (activeVersion is not null)
            {
                activeVersion.Status = RequirementDefinitionVersionStatus.Retired;
            }
        }

        await db.SaveChangesAsync();

        return Ok(definition);
    }

    [HttpPost("{id:guid}/snapshots")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionWrite)]
    public async Task<IActionResult> CreateSnapshot(Guid id, CreateDefinitionSnapshotRequest request)
    {
        var version = await LoadFullVersion(id, request.VersionNumber);
        if (version is null)
            return NotFound();

        var snapshot = new RequirementDefinitionSnapshot
        {
            TenantId = version.TenantId,
            SourceRequirementId = id,
            SourceVersionNumber = request.VersionNumber,
            Reason = request.Reason,
            DefinitionJson = JsonSerializer.Serialize(version),
        };

        db.RequirementDefinitionSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSnapshot), new { id = snapshot.Id }, snapshot);
    }

    [HttpGet("/api/v1/requirement-definition-snapshots/{id:guid}")]
    [RequirePermission(EswmpPermissions.RequirementDefinitionRead)]
    public async Task<IActionResult> GetSnapshot(Guid id)
    {
        var snapshot = await db.RequirementDefinitionSnapshots.FindAsync(id);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    private async Task<RequirementDefinitionVersion?> LoadFullVersion(Guid requirementDefinitionId, int versionNumber) =>
        await db.RequirementDefinitionVersions
            .Include(v => v.ResourceRequirements).ThenInclude(rr => rr.CapabilityRequirements)
            .Include(v => v.ResourceRequirements).ThenInclude(rr => rr.SkillRequirements)
            .Include(v => v.ResourceRequirements).ThenInclude(rr => rr.CertificationRequirements)
            .Include(v => v.LocationConstraints)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.RequirementDefinitionId == requirementDefinitionId && v.VersionNumber == versionNumber);

    private static (string Status, List<string> Issues) RunValidation(RequirementDefinitionVersion version)
    {
        var issues = new List<string>();

        if (version.DurationType == DefinitionDurationType.Fixed)
        {
            if (version.FixedDurationMinutes is null or <= 0)
            {
                issues.Add("FixedDurationMinutes must be a positive value when DurationType is Fixed.");
            }
        }
        else
        {
            if (version.MinimumDurationMinutes is null || version.ExpectedDurationMinutes is null || version.MaximumDurationMinutes is null)
            {
                issues.Add("MinimumDurationMinutes, ExpectedDurationMinutes, and MaximumDurationMinutes are all required when DurationType is Range.");
            }
            else if (!(version.MinimumDurationMinutes <= version.ExpectedDurationMinutes &&
                       version.ExpectedDurationMinutes <= version.MaximumDurationMinutes))
            {
                issues.Add("Duration fields must satisfy Minimum <= Expected <= Maximum.");
            }
        }

        if (version.ResourceRequirements.Count == 0)
        {
            issues.Add("At least one ResourceRequirement is required.");
        }

        var status = issues.Count == 0 ? "Valid" : "Invalid";
        return (status, issues);
    }

    /// <summary>
    /// Builds new child entities and stages them with an explicit <c>DbSet.Add</c> rather
    /// than relying on DetectChanges to infer Added state from graph reachability alone.
    /// Eswmp.Shared's BaseEntity/TenantScopedEntity pre-populate Id via `Guid.NewGuid()` in
    /// a property initializer, so every new entity already has a non-default key — when such
    /// an entity is merely attached to a navigation on an *already-tracked* parent (as in
    /// UpdateVersion, where `version` was loaded from the database, not freshly `Add()`-ed),
    /// EF Core's implicit graph-diffing can't tell "new" from "pre-existing with a client-set
    /// key" and marks it Modified instead of Added, which then fails at SaveChanges time
    /// because no such row exists yet. Calling Add() directly removes the ambiguity.
    /// </summary>
    private void MapResourceRequirements(RequirementDefinitionVersion version, List<DefinitionResourceRequirementDto> dtos)
    {
        foreach (var dto in dtos)
        {
            var resourceRequirement = new DefinitionResourceRequirement
            {
                ResourceTypeCode = dto.ResourceTypeCode,
                Role = dto.Role,
                MinimumQuantity = dto.MinimumQuantity,
                PreferredQuantity = dto.PreferredQuantity,
                MaximumQuantity = dto.MaximumQuantity,
                Mandatory = dto.Mandatory,
            };

            foreach (var capability in dto.Capabilities ?? [])
            {
                var capabilityRequirement = new DefinitionCapabilityRequirement
                {
                    CapabilityCode = capability.CapabilityCode,
                    MinimumLevel = capability.MinimumLevel,
                    Importance = capability.Importance,
                };
                resourceRequirement.CapabilityRequirements.Add(capabilityRequirement);
                db.DefinitionCapabilityRequirements.Add(capabilityRequirement);
            }

            foreach (var skill in dto.Skills ?? [])
            {
                var skillRequirement = new DefinitionSkillRequirement
                {
                    SkillCode = skill.SkillCode,
                    MinimumLevel = skill.MinimumLevel,
                    Mandatory = skill.Mandatory,
                };
                resourceRequirement.SkillRequirements.Add(skillRequirement);
                db.DefinitionSkillRequirements.Add(skillRequirement);
            }

            foreach (var certification in dto.Certifications ?? [])
            {
                var certificationRequirement = new DefinitionCertificationRequirement
                {
                    CertificationTypeCode = certification.CertificationTypeCode,
                    Mandatory = certification.Mandatory,
                };
                resourceRequirement.CertificationRequirements.Add(certificationRequirement);
                db.DefinitionCertificationRequirements.Add(certificationRequirement);
            }

            version.ResourceRequirements.Add(resourceRequirement);
            db.DefinitionResourceRequirements.Add(resourceRequirement);
        }
    }

    private void MapLocationConstraints(RequirementDefinitionVersion version, List<LocationConstraintDto>? dtos)
    {
        foreach (var dto in dtos ?? [])
        {
            var locationConstraint = new LocationConstraint
            {
                Mode = dto.Mode,
                MaximumTravelDistanceKm = dto.MaximumTravelDistanceKm,
                MaximumTravelTimeMinutes = dto.MaximumTravelTimeMinutes,
            };
            version.LocationConstraints.Add(locationConstraint);
            db.LocationConstraints.Add(locationConstraint);
        }
    }
}
