using Eswmp.Assignment.Models;
using Eswmp.Assignment.Services;
using Xunit;

namespace Eswmp.Assignment.Tests;

public class MatchingScorerTests
{
    private readonly MatchingScorer _scorer = new();

    [Fact]
    public void NoCandidates_ReturnsEmpty()
    {
        var result = _scorer.Score(new MatchingWorkContext(), []);

        Assert.Empty(result);
    }

    [Fact]
    public void CloserCandidate_RanksHigherOnDistance()
    {
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();

        var result = _scorer.Score(
            new MatchingWorkContext { TargetLatitude = 43.65, TargetLongitude = -79.38 },
            [
                new MatchingCandidate { CandidateType = "Resource", CandidateId = near, Latitude = 43.651, Longitude = -79.381 },
                new MatchingCandidate { CandidateType = "Resource", CandidateId = far, Latitude = 45.42, Longitude = -75.69 },
            ]);

        Assert.Equal(near, result[0].CandidateId);
        Assert.True(result[0].NormalizedScore > result[1].NormalizedScore);
    }

    [Fact]
    public void FullSkillOverlap_RanksHigherThanPartialOverlap()
    {
        var fullMatch = Guid.NewGuid();
        var partialMatch = Guid.NewGuid();

        var result = _scorer.Score(
            new MatchingWorkContext { RequiredSkills = ["Welding", "Electrical"] },
            [
                new MatchingCandidate { CandidateType = "Resource", CandidateId = fullMatch, Skills = ["Welding", "Electrical"] },
                new MatchingCandidate { CandidateType = "Resource", CandidateId = partialMatch, Skills = ["Welding"] },
            ]);

        var fullScore = result.Single(c => c.CandidateId == fullMatch).NormalizedScore;
        var partialScore = result.Single(c => c.CandidateId == partialMatch).NormalizedScore;

        Assert.True(fullScore > partialScore);
        Assert.Equal(fullMatch, result[0].CandidateId);
    }

    [Fact]
    public void LowerWorkload_RanksHigherThanBusyCandidate()
    {
        var idle = Guid.NewGuid();
        var busy = Guid.NewGuid();

        var result = _scorer.Score(
            new MatchingWorkContext(),
            [
                new MatchingCandidate { CandidateType = "Resource", CandidateId = idle, CurrentWorkload = 0 },
                new MatchingCandidate { CandidateType = "Resource", CandidateId = busy, CurrentWorkload = 10 },
            ]);

        var idleScore = result.Single(c => c.CandidateId == idle).NormalizedScore;
        var busyScore = result.Single(c => c.CandidateId == busy).NormalizedScore;

        Assert.True(idleScore > busyScore);
    }

    [Fact]
    public void MissingLocation_UsesNeutralScore_NotZero()
    {
        var candidateId = Guid.NewGuid();

        var result = _scorer.Score(
            new MatchingWorkContext { TargetLatitude = 43.65, TargetLongitude = -79.38 },
            [new MatchingCandidate { CandidateType = "Resource", CandidateId = candidateId }]);

        var distanceFactor = result.Single().Factors.Single(f => f.FactorCode == MatchingScorer.DistanceFactorCode);

        Assert.Equal(MatchingScorer.NeutralScore, distanceFactor.NormalizedScore);
        Assert.NotEqual(0, distanceFactor.NormalizedScore);
    }

    [Fact]
    public void ResultsAreOrderedDescendingByScore_AndRankedSequentially()
    {
        var result = _scorer.Score(
            new MatchingWorkContext(),
            [
                new MatchingCandidate { CandidateType = "Resource", CandidateId = Guid.NewGuid(), CurrentWorkload = 5 },
                new MatchingCandidate { CandidateType = "Resource", CandidateId = Guid.NewGuid(), CurrentWorkload = 0 },
                new MatchingCandidate { CandidateType = "Resource", CandidateId = Guid.NewGuid(), CurrentWorkload = 2 },
            ]);

        Assert.True(result[0].NormalizedScore >= result[1].NormalizedScore);
        Assert.True(result[1].NormalizedScore >= result[2].NormalizedScore);
        Assert.Equal([1, 2, 3], result.Select(r => r.Rank));
    }

    [Theory]
    [InlineData(95, RecommendationLevel.ExcellentMatch)]
    [InlineData(90, RecommendationLevel.ExcellentMatch)]
    [InlineData(89.99, RecommendationLevel.StrongMatch)]
    [InlineData(80, RecommendationLevel.StrongMatch)]
    [InlineData(79.99, RecommendationLevel.GoodMatch)]
    [InlineData(65, RecommendationLevel.GoodMatch)]
    [InlineData(64.99, RecommendationLevel.PossibleMatch)]
    [InlineData(50, RecommendationLevel.PossibleMatch)]
    [InlineData(49.99, RecommendationLevel.LowMatch)]
    [InlineData(0, RecommendationLevel.LowMatch)]
    public void ToRecommendationLevel_MapsThresholdsPerSpec(double score, RecommendationLevel expected)
    {
        Assert.Equal(expected, MatchingScorer.ToRecommendationLevel(score));
    }

    [Fact]
    public void CustomWeights_AreHonoured_SkillOnlyPolicyIgnoresDistanceAndWorkload()
    {
        var skilled = Guid.NewGuid();
        var unskilled = Guid.NewGuid();

        var weights = new[]
        {
            new MatchFactorWeight { FactorCode = MatchingScorer.SkillMatchFactorCode, Weight = 1.0, Enabled = true },
            new MatchFactorWeight { FactorCode = MatchingScorer.DistanceFactorCode, Weight = 0, Enabled = false },
            new MatchFactorWeight { FactorCode = MatchingScorer.WorkloadBalanceFactorCode, Weight = 0, Enabled = false },
        };

        var result = _scorer.Score(
            new MatchingWorkContext { RequiredSkills = ["Certified"] },
            [
                new MatchingCandidate { CandidateType = "Resource", CandidateId = skilled, Skills = ["Certified"], CurrentWorkload = 100 },
                new MatchingCandidate { CandidateType = "Resource", CandidateId = unskilled, Skills = [], CurrentWorkload = 0 },
            ],
            weights);

        Assert.Equal(skilled, result[0].CandidateId);
        Assert.Equal(100, result[0].NormalizedScore);
        Assert.Equal(0, result[1].NormalizedScore);
    }
}
