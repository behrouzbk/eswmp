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

[ApiController]
[Route("api/v1/resources")]
public class ResourcesController(CoreDbContext db, ITenantContext tenantContext) : ControllerBase
{
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
}
