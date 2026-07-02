using Eswmp.Assignment.Data;
using Eswmp.Assignment.Models;
using Eswmp.Assignment.Services;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace Eswmp.Assignment.Controllers;

public record ScoreAssignmentRequest(
    Guid ReservationId,
    double? TargetLatitude,
    double? TargetLongitude,
    string[]? RequiredSkills,
    int? EstimatedDurationMinutes,
    IReadOnlyList<AssignmentCandidate> Candidates);

public record ScoreAssignmentResponse(Guid ReservationId, IReadOnlyList<ScoredCandidate> RankedCandidates);

public record CreateAssignmentLogRequest(Guid ReservationId, Guid ResourceId, AssignmentMethod Method, double? Score);

[ApiController]
[Route("api/v1/assignments")]
public class AssignmentController(
    AssignmentScorer scorer,
    AssignmentDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("score")]
    [RequirePermission(EswmpPermissions.AssignmentRead)]
    public ActionResult<ScoreAssignmentResponse> Score(ScoreAssignmentRequest request)
    {
        var ranked = scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = request.ReservationId,
            TargetLatitude = request.TargetLatitude,
            TargetLongitude = request.TargetLongitude,
            RequiredSkills = request.RequiredSkills ?? [],
            EstimatedDurationMinutes = request.EstimatedDurationMinutes,
            Candidates = request.Candidates,
        });

        return Ok(new ScoreAssignmentResponse(request.ReservationId, ranked));
    }

    [HttpPost]
    [RequirePermission(EswmpPermissions.AssignmentExecute)]
    public async Task<IActionResult> Create(CreateAssignmentLogRequest request)
    {
        var log = new AssignmentLog
        {
            TenantId = tenantContext.RequiredTenantId,
            ReservationId = request.ReservationId,
            ResourceId = request.ResourceId,
            Method = request.Method,
            Score = request.Score,
        };

        db.AssignmentLogs.Add(log);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Create), new { id = log.Id }, log);
    }
}
