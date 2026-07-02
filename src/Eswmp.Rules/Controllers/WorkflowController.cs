using Eswmp.Rules.Models;
using Eswmp.Rules.Services;
using Eswmp.Shared.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Eswmp.Rules.Controllers;

public record ValidateTransitionRequest(WorkflowState FromState, WorkflowState ToState);
public record ValidateTransitionResponse(bool IsValid, string? Reason);

[ApiController]
[Route("api/v1/workflow")]
public class WorkflowController(WorkflowTransitionValidator validator) : ControllerBase
{
    [HttpPost("transitions/validate")]
    [RequirePermission(EswmpPermissions.WorkflowTransition)]
    public ActionResult<ValidateTransitionResponse> ValidateTransition(ValidateTransitionRequest request)
    {
        var (isValid, reason) = validator.Validate(request.FromState, request.ToState);
        return Ok(new ValidateTransitionResponse(isValid, reason));
    }
}
