namespace Eswmp.Rules.Models;

/// <summary>
/// The full reservation/appointment lifecycle. Wider than the four-state
/// Held/Confirmed/Expired/Cancelled model in Eswmp.Core — this is the workflow
/// a consuming product's UI would surface (arrival tracking, approval steps),
/// layered on top of Core's simpler reservation status. See docs/ESWMP_VISION.md §9.
/// </summary>
public enum WorkflowState
{
    Draft,
    Requested,
    Reserved,
    PendingApproval,
    Assigned,
    Confirmed,
    Travelling,
    Arrived,
    CheckedIn,
    InProgress,
    Completed,
    Cancelled,
    NoShow,
    Rejected,
    Expired
}
