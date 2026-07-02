using Eswmp.Assignment.Services;
using Xunit;

namespace Eswmp.Assignment.Tests;

public class AssignmentScorerTests
{
    private readonly AssignmentScorer _scorer = new();

    [Fact]
    public void NoCandidates_ReturnsEmpty()
    {
        var result = _scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = Guid.NewGuid(),
            Candidates = [],
        });

        Assert.Empty(result);
    }

    [Fact]
    public void CloserCandidate_RanksHigherOnProximity()
    {
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();

        var result = _scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = Guid.NewGuid(),
            TargetLatitude = 43.65,
            TargetLongitude = -79.38,
            Candidates =
            [
                new AssignmentCandidate { ResourceId = near, CurrentLatitude = 43.651, CurrentLongitude = -79.381 },
                new AssignmentCandidate { ResourceId = far, CurrentLatitude = 45.42, CurrentLongitude = -75.69 },
            ],
        });

        Assert.Equal(near, result[0].ResourceId);
    }

    [Fact]
    public void MissingRequiredSkill_ScoresLowerThanMatchingCandidate()
    {
        var skilled = Guid.NewGuid();
        var unskilled = Guid.NewGuid();

        var result = _scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = Guid.NewGuid(),
            RequiredSkills = ["Certified"],
            Candidates =
            [
                new AssignmentCandidate { ResourceId = skilled, Skills = ["Certified"] },
                new AssignmentCandidate { ResourceId = unskilled, Skills = [] },
            ],
        });

        var skilledScore = result.Single(c => c.ResourceId == skilled).Score;
        var unskilledScore = result.Single(c => c.ResourceId == unskilled).Score;

        Assert.True(skilledScore > unskilledScore);
        Assert.True(result.Single(c => c.ResourceId == skilled).SkillMatch);
        Assert.False(result.Single(c => c.ResourceId == unskilled).SkillMatch);
    }

    [Fact]
    public void LowerWorkload_RanksHigherThanBusyCandidate()
    {
        var idle = Guid.NewGuid();
        var busy = Guid.NewGuid();

        var result = _scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = Guid.NewGuid(),
            Candidates =
            [
                new AssignmentCandidate { ResourceId = idle, ActiveReservationCount = 0 },
                new AssignmentCandidate { ResourceId = busy, ActiveReservationCount = 10 },
            ],
        });

        var idleScore = result.Single(c => c.ResourceId == idle).Score;
        var busyScore = result.Single(c => c.ResourceId == busy).Score;

        Assert.True(idleScore > busyScore);
    }

    [Fact]
    public void ResultsAreOrderedDescendingByScore()
    {
        var result = _scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = Guid.NewGuid(),
            Candidates =
            [
                new AssignmentCandidate { ResourceId = Guid.NewGuid(), ActiveReservationCount = 5 },
                new AssignmentCandidate { ResourceId = Guid.NewGuid(), ActiveReservationCount = 0 },
                new AssignmentCandidate { ResourceId = Guid.NewGuid(), ActiveReservationCount = 2 },
            ],
        });

        Assert.True(result[0].Score >= result[1].Score);
        Assert.True(result[1].Score >= result[2].Score);
    }

    [Fact]
    public void NoLocationData_DoesNotThrow_UsesNeutralProximityScore()
    {
        var result = _scorer.Score(new ScoreAssignmentInput
        {
            ReservationId = Guid.NewGuid(),
            Candidates = [new AssignmentCandidate { ResourceId = Guid.NewGuid() }],
        });

        Assert.Single(result);
        Assert.Null(result[0].DistanceMetres);
    }
}
