using Eswmp.Rules.Data;
using Eswmp.Rules.Models;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Eswmp.Rules.Controllers;

public record CreateBusinessRuleRequest(
    string Name,
    string RuleType,
    string? ResourceType,
    JsonElement Definition,
    bool IsActive = true);

[ApiController]
[Route("api/v1/rules")]
public class RulesController(RulesDbContext db, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost]
    [RequirePermission(EswmpPermissions.RuleWrite)]
    public async Task<IActionResult> Create(CreateBusinessRuleRequest request)
    {
        var rule = new BusinessRule
        {
            TenantId = tenantContext.RequiredTenantId,
            Name = request.Name,
            RuleType = request.RuleType,
            ResourceType = request.ResourceType,
            DefinitionJson = request.Definition.GetRawText(),
            IsActive = request.IsActive,
        };

        db.BusinessRules.Add(rule);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Create), new { id = rule.Id }, rule);
    }

    [HttpGet]
    [RequirePermission(EswmpPermissions.RuleRead)]
    public async Task<IActionResult> List([FromQuery] string? ruleType)
    {
        var query = db.BusinessRules.Where(r => r.IsActive);

        if (!string.IsNullOrWhiteSpace(ruleType))
            query = query.Where(r => r.RuleType == ruleType);

        return Ok(await query.ToListAsync());
    }
}
