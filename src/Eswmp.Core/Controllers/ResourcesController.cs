using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Auth;
using Eswmp.Shared.DTOs;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eswmp.Core.Controllers;

public record CreateResourceRequest(
    string ResourceType,
    string Name,
    string Timezone,
    int? Capacity,
    string[]? Skills,
    double? LocationLatitude,
    double? LocationLongitude);

public record ResourceLifecycleRequest(string? ReasonCode, string? Comment, int? ExpectedVersion);

public record AddResourceCapabilityRequest(string CapabilityCode, int Level, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);
public record AddResourceSkillRequest(string SkillCode, int Level, decimal? YearsOfExperience, bool Verified);
public record AddResourceCertificationRequest(string CertificationTypeCode, string? CredentialReference, DateOnly? IssuedAt, DateOnly? ExpiresAt);

[ApiController]
[Route("api/v1/resources")]
public class ResourcesController(CoreDbContext db, ITenantContext tenantContext) : ControllerBase
{
    // Valid lifecycle transitions — see architecture-reconciliation plan Task 2.
    private static readonly IReadOnlyDictionary<string, ResourceStatus[]> AllowedFrom = new Dictionary<string, ResourceStatus[]>
    {
        ["activate"] = [ResourceStatus.Draft, ResourceStatus.PendingVerification, ResourceStatus.Inactive],
        ["suspend"] = [ResourceStatus.Active],
        ["reactivate"] = [ResourceStatus.Suspended],
        ["deactivate"] = [ResourceStatus.Active, ResourceStatus.Suspended],
        ["retire"] = [ResourceStatus.Draft, ResourceStatus.PendingVerification, ResourceStatus.Active, ResourceStatus.Suspended, ResourceStatus.Inactive],
    };

    private static readonly IReadOnlyDictionary<string, ResourceStatus> TargetStatus = new Dictionary<string, ResourceStatus>
    {
        ["activate"] = ResourceStatus.Active,
        ["suspend"] = ResourceStatus.Suspended,
        ["reactivate"] = ResourceStatus.Active,
        ["deactivate"] = ResourceStatus.Inactive,
        ["retire"] = ResourceStatus.Retired,
    };

    [HttpPost]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public async Task<IActionResult> Create(CreateResourceRequest request)
    {
        var resource = new Resource
        {
            TenantId = tenantContext.RequiredTenantId,
            ResourceType = request.ResourceType,
            Name = request.Name,
            Timezone = request.Timezone,
            Capacity = request.Capacity,
            Skills = request.Skills is { Length: > 0 } ? string.Join(',', request.Skills) : null,
            LocationLatitude = request.LocationLatitude,
            LocationLongitude = request.LocationLongitude,
        };

        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = resource.Id }, resource);
    }

    [HttpGet]
    [RequirePermission(EswmpPermissions.ResourceRead)]
    public async Task<ActionResult<PagedResult<Resource>>> List(
        [FromQuery] string? resourceType,
        [FromQuery] ResourceStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var query = db.Resources.AsQueryable();

        if (!string.IsNullOrWhiteSpace(resourceType))
            query = query.Where(r => r.ResourceType == resourceType);

        if (status is not null)
            query = query.Where(r => r.Status == status);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<Resource>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.ResourceRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var resource = await db.Resources.FindAsync(id);
        return resource is null ? NotFound() : Ok(resource);
    }

    [HttpPost("{id:guid}/activate")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public Task<IActionResult> Activate(Guid id, ResourceLifecycleRequest request) => Transition(id, "activate", request);

    [HttpPost("{id:guid}/suspend")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public Task<IActionResult> Suspend(Guid id, ResourceLifecycleRequest request) => Transition(id, "suspend", request);

    [HttpPost("{id:guid}/reactivate")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public Task<IActionResult> Reactivate(Guid id, ResourceLifecycleRequest request) => Transition(id, "reactivate", request);

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public Task<IActionResult> Deactivate(Guid id, ResourceLifecycleRequest request) => Transition(id, "deactivate", request);

    [HttpPost("{id:guid}/retire")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public Task<IActionResult> Retire(Guid id, ResourceLifecycleRequest request) => Transition(id, "retire", request);

    private async Task<IActionResult> Transition(Guid id, string action, ResourceLifecycleRequest request)
    {
        var resource = await db.Resources.FindAsync(id);
        if (resource is null)
            return NotFound();

        if (request.ExpectedVersion is not null && request.ExpectedVersion.Value != resource.Version)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, new
            {
                error = $"Expected version {request.ExpectedVersion} does not match current version {resource.Version}.",
            });
        }

        if (!AllowedFrom[action].Contains(resource.Status))
        {
            return Conflict(new { error = $"Cannot {action} a Resource in status {resource.Status}." });
        }

        resource.Status = TargetStatus[action];
        resource.Version++;
        await db.SaveChangesAsync();

        return Ok(resource);
    }

    [HttpPost("{id:guid}/capabilities")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public async Task<IActionResult> AddCapability(Guid id, AddResourceCapabilityRequest request)
    {
        var resource = await db.Resources.FindAsync(id);
        if (resource is null)
            return NotFound();

        var capability = new ResourceCapability
        {
            TenantId = resource.TenantId,
            ResourceId = id,
            CapabilityCode = request.CapabilityCode,
            Level = request.Level,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };

        db.ResourceCapabilities.Add(capability);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id }, capability);
    }

    [HttpPost("{id:guid}/skills")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public async Task<IActionResult> AddSkill(Guid id, AddResourceSkillRequest request)
    {
        var resource = await db.Resources.FindAsync(id);
        if (resource is null)
            return NotFound();

        var skill = new ResourceSkill
        {
            TenantId = resource.TenantId,
            ResourceId = id,
            SkillCode = request.SkillCode,
            Level = request.Level,
            YearsOfExperience = request.YearsOfExperience,
            Verified = request.Verified,
        };

        db.ResourceSkills.Add(skill);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id }, skill);
    }

    [HttpPost("{id:guid}/certifications")]
    [RequirePermission(EswmpPermissions.ResourceWrite)]
    public async Task<IActionResult> AddCertification(Guid id, AddResourceCertificationRequest request)
    {
        var resource = await db.Resources.FindAsync(id);
        if (resource is null)
            return NotFound();

        var certification = new ResourceCertification
        {
            TenantId = resource.TenantId,
            ResourceId = id,
            CertificationTypeCode = request.CertificationTypeCode,
            CredentialReference = request.CredentialReference,
            IssuedAt = request.IssuedAt,
            ExpiresAt = request.ExpiresAt,
        };

        db.ResourceCertifications.Add(certification);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id }, certification);
    }
}
