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

public record CreateWorkRequirementRequest(string Code, string Name, string? Description, string? Category);

public record WorkRequirementSearchRequest(
    WorkRequirementStatus? Status,
    string? Category,
    int Page = 1,
    int PageSize = 20);

public record CapabilityRequirementDto(string CapabilityCode, int? MinimumLevel, CapabilityImportance Importance);
public record SkillRequirementDto(string SkillCode, int? MinimumLevel, bool Mandatory);
public record CertificationRequirementDto(string CertificationTypeCode, bool Mandatory);

public record ResourceRequirementDto(
    string ResourceTypeCode,
    string? Role,
    int MinimumQuantity,
    int PreferredQuantity,
    int MaximumQuantity,
    bool Mandatory,
    List<CapabilityRequirementDto>? Capabilities,
    List<SkillRequirementDto>? Skills,
    List<CertificationRequirementDto>? Certifications);

public record LocationConstraintDto(
    LocationConstraintMode Mode,
    decimal? MaximumTravelDistanceKm,
    int? MaximumTravelTimeMinutes);

public record RequirementVersionRequest(
    string? ChangeSummary,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    DurationType DurationType,
    int? FixedDurationMinutes,
    int? MinimumDurationMinutes,
    int? ExpectedDurationMinutes,
    int? MaximumDurationMinutes,
    int PreWorkBufferMinutes,
    int PostWorkBufferMinutes,
    List<ResourceRequirementDto> ResourceRequirements,
    List<LocationConstraintDto>? LocationConstraints);

public record ActivateVersionRequest(int ExpectedVersion);

public record CreateSnapshotRequest(int VersionNumber, string? Reason);

[ApiController]
[Route("api/v1/work-requirements")]
public class WorkRequirementsController(
    WorkDbContext db,
    ITenantContext tenantContext,
    IPublishEndpoint publishEndpoint) : ControllerBase
{
    [HttpPost]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> Create(CreateWorkRequirementRequest request)
    {
        var tenantId = tenantContext.RequiredTenantId;

        var codeExists = await db.WorkRequirements.AnyAsync(w => w.Code == request.Code);
        if (codeExists)
        {
            return Conflict(new { error = $"Code '{request.Code}' is already in use for this tenant." });
        }

        var workRequirement = new WorkRequirement
        {
            TenantId = tenantId,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
        };

        db.WorkRequirements.Add(workRequirement);
        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new WorkRequirementCreatedEvent(
            workRequirement.Id, workRequirement.TenantId, workRequirement.Code, Guid.NewGuid()));

        return CreatedAtAction(nameof(GetById), new { id = workRequirement.Id }, workRequirement);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var workRequirement = await db.WorkRequirements
            .Include(w => w.Versions).ThenInclude(v => v.LocationConstraints)
            .Include(w => w.Versions).ThenInclude(v => v.ResourceRequirements).ThenInclude(rr => rr.CapabilityRequirements)
            .Include(w => w.Versions).ThenInclude(v => v.ResourceRequirements).ThenInclude(rr => rr.SkillRequirements)
            .Include(w => w.Versions).ThenInclude(v => v.ResourceRequirements).ThenInclude(rr => rr.CertificationRequirements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id);

        return workRequirement is null ? NotFound() : Ok(workRequirement);
    }

    [HttpPost("search")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> Search(WorkRequirementSearchRequest request)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var query = db.WorkRequirements.AsQueryable();

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

        return Ok(new PagedResult<WorkRequirement>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    [HttpPost("{id:guid}/versions")]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> CreateVersion(Guid id, RequirementVersionRequest request)
    {
        var workRequirement = await db.WorkRequirements.FirstOrDefaultAsync(w => w.Id == id);
        if (workRequirement is null)
            return NotFound();

        var versionNumber = workRequirement.CurrentVersionNumber + 1;

        var version = new RequirementVersion
        {
            TenantId = workRequirement.TenantId,
            WorkRequirementId = workRequirement.Id,
            VersionNumber = versionNumber,
            Status = RequirementVersionStatus.Draft,
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

        db.RequirementVersions.Add(version);
        workRequirement.CurrentVersionNumber = versionNumber;

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetVersion), new { id = workRequirement.Id, versionNumber }, version);
    }

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> GetVersion(Guid id, int versionNumber)
    {
        var version = await LoadFullVersion(id, versionNumber);
        return version is null ? NotFound() : Ok(version);
    }

    [HttpPatch("{id:guid}/versions/{versionNumber:int}")]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> UpdateVersion(Guid id, int versionNumber, RequirementVersionRequest request)
    {
        var version = await LoadFullVersion(id, versionNumber);
        if (version is null)
            return NotFound();

        if (version.Status != RequirementVersionStatus.Draft)
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
            db.CapabilityRequirements.RemoveRange(resourceRequirement.CapabilityRequirements);
            db.SkillRequirements.RemoveRange(resourceRequirement.SkillRequirements);
            db.CertificationRequirements.RemoveRange(resourceRequirement.CertificationRequirements);
        }

        db.ResourceRequirements.RemoveRange(version.ResourceRequirements);
        db.LocationConstraints.RemoveRange(version.LocationConstraints);
        version.ResourceRequirements = [];
        version.LocationConstraints = [];

        MapResourceRequirements(version, request.ResourceRequirements);
        MapLocationConstraints(version, request.LocationConstraints);

        await db.SaveChangesAsync();

        return Ok(version);
    }

    [HttpPost("{id:guid}/versions/{versionNumber:int}/validate")]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> ValidateVersion(Guid id, int versionNumber)
    {
        var version = await db.RequirementVersions
            .Include(v => v.ResourceRequirements)
            .FirstOrDefaultAsync(v => v.WorkRequirementId == id && v.VersionNumber == versionNumber);

        if (version is null)
            return NotFound();

        var (status, issues) = RunValidation(version);

        if (status == "Valid" && version.Status == RequirementVersionStatus.Draft)
        {
            version.Status = RequirementVersionStatus.Validated;
            await db.SaveChangesAsync();
        }

        return Ok(new { Status = status, Issues = issues });
    }

    [HttpPost("{id:guid}/versions/{versionNumber:int}/activate")]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> ActivateVersion(Guid id, int versionNumber, ActivateVersionRequest request)
    {
        var workRequirement = await db.WorkRequirements.FirstOrDefaultAsync(w => w.Id == id);
        if (workRequirement is null)
            return NotFound();

        if (request.ExpectedVersion != workRequirement.ConcurrencyVersion)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, new
            {
                error = $"Expected version {request.ExpectedVersion} does not match current version {workRequirement.ConcurrencyVersion}."
            });
        }

        var version = await db.RequirementVersions
            .Include(v => v.ResourceRequirements)
            .FirstOrDefaultAsync(v => v.WorkRequirementId == id && v.VersionNumber == versionNumber);

        if (version is null)
            return NotFound();

        if (version.Status != RequirementVersionStatus.Draft && version.Status != RequirementVersionStatus.Validated)
        {
            return Conflict(new { error = $"Version {versionNumber} is {version.Status}; only Draft or Validated versions may be activated." });
        }

        if (version.Status == RequirementVersionStatus.Draft)
        {
            var (status, issues) = RunValidation(version);
            if (status != "Valid")
            {
                return UnprocessableEntity(new { Status = status, Issues = issues });
            }
        }

        if (workRequirement.ActiveVersionNumber is not null)
        {
            var priorActive = await db.RequirementVersions.FirstOrDefaultAsync(
                v => v.WorkRequirementId == id && v.VersionNumber == workRequirement.ActiveVersionNumber);
            if (priorActive is not null)
            {
                priorActive.Status = RequirementVersionStatus.Superseded;
            }
        }

        version.Status = RequirementVersionStatus.Active;
        workRequirement.ActiveVersionNumber = versionNumber;
        workRequirement.Status = WorkRequirementStatus.Active;
        workRequirement.ConcurrencyVersion++;

        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new RequirementVersionActivatedEvent(
            workRequirement.Id, versionNumber, workRequirement.TenantId, Guid.NewGuid()));

        return Ok(version);
    }

    [HttpPost("{id:guid}/retire")]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> Retire(Guid id)
    {
        var workRequirement = await db.WorkRequirements.FirstOrDefaultAsync(w => w.Id == id);
        if (workRequirement is null)
            return NotFound();

        if (workRequirement.Status == WorkRequirementStatus.Retired)
        {
            return Conflict(new { error = "WorkRequirement is already Retired." });
        }

        workRequirement.Status = WorkRequirementStatus.Retired;
        workRequirement.ConcurrencyVersion++;

        if (workRequirement.ActiveVersionNumber is not null)
        {
            var activeVersion = await db.RequirementVersions.FirstOrDefaultAsync(
                v => v.WorkRequirementId == id && v.VersionNumber == workRequirement.ActiveVersionNumber);
            if (activeVersion is not null)
            {
                activeVersion.Status = RequirementVersionStatus.Retired;
            }
        }

        await db.SaveChangesAsync();

        return Ok(workRequirement);
    }

    [HttpPost("{id:guid}/snapshots")]
    [RequirePermission(EswmpPermissions.WorkRequirementWrite)]
    public async Task<IActionResult> CreateSnapshot(Guid id, CreateSnapshotRequest request)
    {
        var version = await LoadFullVersion(id, request.VersionNumber);
        if (version is null)
            return NotFound();

        var snapshot = new RequirementSnapshot
        {
            TenantId = version.TenantId,
            SourceRequirementId = id,
            SourceVersionNumber = request.VersionNumber,
            Reason = request.Reason,
            DefinitionJson = JsonSerializer.Serialize(version),
        };

        db.RequirementSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSnapshot), new { id = snapshot.Id }, snapshot);
    }

    [HttpGet("/api/v1/work-requirement-snapshots/{id:guid}")]
    [RequirePermission(EswmpPermissions.WorkRequirementRead)]
    public async Task<IActionResult> GetSnapshot(Guid id)
    {
        var snapshot = await db.RequirementSnapshots.FindAsync(id);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    private async Task<RequirementVersion?> LoadFullVersion(Guid workRequirementId, int versionNumber) =>
        await db.RequirementVersions
            .Include(v => v.ResourceRequirements).ThenInclude(rr => rr.CapabilityRequirements)
            .Include(v => v.ResourceRequirements).ThenInclude(rr => rr.SkillRequirements)
            .Include(v => v.ResourceRequirements).ThenInclude(rr => rr.CertificationRequirements)
            .Include(v => v.LocationConstraints)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.WorkRequirementId == workRequirementId && v.VersionNumber == versionNumber);

    private static (string Status, List<string> Issues) RunValidation(RequirementVersion version)
    {
        var issues = new List<string>();

        if (version.DurationType == DurationType.Fixed)
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
    private void MapResourceRequirements(RequirementVersion version, List<ResourceRequirementDto> dtos)
    {
        foreach (var dto in dtos)
        {
            var resourceRequirement = new ResourceRequirement
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
                var capabilityRequirement = new CapabilityRequirement
                {
                    CapabilityCode = capability.CapabilityCode,
                    MinimumLevel = capability.MinimumLevel,
                    Importance = capability.Importance,
                };
                resourceRequirement.CapabilityRequirements.Add(capabilityRequirement);
                db.CapabilityRequirements.Add(capabilityRequirement);
            }

            foreach (var skill in dto.Skills ?? [])
            {
                var skillRequirement = new SkillRequirement
                {
                    SkillCode = skill.SkillCode,
                    MinimumLevel = skill.MinimumLevel,
                    Mandatory = skill.Mandatory,
                };
                resourceRequirement.SkillRequirements.Add(skillRequirement);
                db.SkillRequirements.Add(skillRequirement);
            }

            foreach (var certification in dto.Certifications ?? [])
            {
                var certificationRequirement = new CertificationRequirement
                {
                    CertificationTypeCode = certification.CertificationTypeCode,
                    Mandatory = certification.Mandatory,
                };
                resourceRequirement.CertificationRequirements.Add(certificationRequirement);
                db.CertificationRequirements.Add(certificationRequirement);
            }

            version.ResourceRequirements.Add(resourceRequirement);
            db.ResourceRequirements.Add(resourceRequirement);
        }
    }

    private void MapLocationConstraints(RequirementVersion version, List<LocationConstraintDto>? dtos)
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
