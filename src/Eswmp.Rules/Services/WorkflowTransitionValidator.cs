using Eswmp.Rules.Models;

namespace Eswmp.Rules.Services;

/// <summary>
/// Guard-clause state machine over <see cref="WorkflowState"/> — same
/// lightweight approach PetZiv's Operations cluster used for its Job lifecycle
/// (a simple enum + transition table, no state-machine library needed at this
/// scale). Terminal states have no outgoing transitions.
/// </summary>
public class WorkflowTransitionValidator
{
    private static readonly Dictionary<WorkflowState, WorkflowState[]> AllowedTransitions = new()
    {
        [WorkflowState.Draft] = [WorkflowState.Requested, WorkflowState.Cancelled],
        [WorkflowState.Requested] = [WorkflowState.Reserved, WorkflowState.Rejected, WorkflowState.Cancelled, WorkflowState.Expired],
        [WorkflowState.Reserved] = [WorkflowState.PendingApproval, WorkflowState.Assigned, WorkflowState.Cancelled, WorkflowState.Expired],
        [WorkflowState.PendingApproval] = [WorkflowState.Assigned, WorkflowState.Rejected, WorkflowState.Cancelled, WorkflowState.Expired],
        [WorkflowState.Assigned] = [WorkflowState.Confirmed, WorkflowState.Cancelled],
        [WorkflowState.Confirmed] = [WorkflowState.Travelling, WorkflowState.CheckedIn, WorkflowState.InProgress, WorkflowState.Cancelled, WorkflowState.NoShow],
        [WorkflowState.Travelling] = [WorkflowState.Arrived, WorkflowState.Cancelled, WorkflowState.NoShow],
        [WorkflowState.Arrived] = [WorkflowState.CheckedIn, WorkflowState.InProgress, WorkflowState.NoShow],
        [WorkflowState.CheckedIn] = [WorkflowState.InProgress, WorkflowState.NoShow],
        [WorkflowState.InProgress] = [WorkflowState.Completed, WorkflowState.Cancelled],
        [WorkflowState.Completed] = [],
        [WorkflowState.Cancelled] = [],
        [WorkflowState.NoShow] = [],
        [WorkflowState.Rejected] = [],
        [WorkflowState.Expired] = [],
    };

    public (bool IsValid, string? Reason) Validate(WorkflowState fromState, WorkflowState toState)
    {
        if (fromState == toState)
            return (false, "fromState and toState are the same.");

        if (!AllowedTransitions.TryGetValue(fromState, out var allowed))
            return (false, $"Unknown state {fromState}.");

        if (!allowed.Contains(toState))
            return (false, $"{fromState} cannot transition to {toState}. Allowed: {(allowed.Length == 0 ? "none (terminal state)" : string.Join(", ", allowed))}.");

        return (true, null);
    }
}
