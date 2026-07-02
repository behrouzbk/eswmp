using Eswmp.Core.Models;
using Eswmp.Core.Services;
using Xunit;

namespace Eswmp.Core.Tests;

public class ReservationDurationEstimatorTests
{
    private readonly ReservationDurationEstimator _estimator = new();

    private static DurationSizeBracket Bracket(decimal maxSize, int minutes) => new()
    {
        TenantId = Guid.NewGuid(),
        ResourceType = "TestType",
        MaxSizeValue = maxSize,
        BaseMinutes = minutes,
    };

    private static DurationTagRule TagRule(string tag, int additionalMinutes = 0, decimal? multiplierPercent = null, string? safetyAlert = null) => new()
    {
        TenantId = Guid.NewGuid(),
        ResourceType = null,
        Tag = tag,
        AdditionalMinutes = additionalMinutes,
        MultiplierPercent = multiplierPercent,
        SafetyAlertMessage = safetyAlert,
    };

    [Fact]
    public void NoSizeValue_UsesDefaultBaseMinutes()
    {
        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType" },
            sizeBrackets: [],
            tagRules: [],
            defaultBaseMinutes: 45);

        Assert.Equal(45, result.BaseMinutes);
        Assert.Equal(45, result.EstimatedMinutes);
    }

    [Fact]
    public void SizeValue_MatchesSmallestApplicableBracket()
    {
        var brackets = new[] { Bracket(5, 30), Bracket(15, 45), Bracket(30, 60) };

        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType", SizeValue = 10 },
            brackets,
            tagRules: []);

        Assert.Equal(45, result.BaseMinutes);
    }

    [Fact]
    public void TagRule_AddsBufferMinutes()
    {
        var tagRules = new[] { TagRule("Anxious", additionalMinutes: 15) };

        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType", AttributeTags = ["Anxious"] },
            sizeBrackets: [],
            tagRules,
            defaultBaseMinutes: 60);

        Assert.Equal(15, result.BufferMinutes);
        Assert.Equal(75, result.EstimatedMinutes);
    }

    [Fact]
    public void TagRule_RaisesSafetyAlert()
    {
        var tagRules = new[] { TagRule("Aggressive", additionalMinutes: 20, safetyAlert: "Safety review required.") };

        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType", AttributeTags = ["Aggressive"] },
            sizeBrackets: [],
            tagRules,
            defaultBaseMinutes: 60);

        Assert.True(result.RequiresSafetyAlert);
        Assert.Equal("Safety review required.", result.SafetyAlertReason);
    }

    [Fact]
    public void TagRule_AppliesMultiplier()
    {
        var tagRules = new[] { TagRule("Matted", multiplierPercent: 150) };

        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType", AttributeTags = ["Matted"] },
            sizeBrackets: [],
            tagRules,
            defaultBaseMinutes: 60);

        Assert.Equal(90, result.BaseMinutes);
    }

    [Fact]
    public void MultipleTags_StackBuffers()
    {
        var tagRules = new[]
        {
            TagRule("Anxious", additionalMinutes: 15),
            TagRule("FirstVisit", additionalMinutes: 10),
        };

        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType", AttributeTags = ["Anxious", "FirstVisit"] },
            sizeBrackets: [],
            tagRules,
            defaultBaseMinutes: 60);

        Assert.Equal(25, result.BufferMinutes);
    }

    [Fact]
    public void TagRuleForDifferentResourceType_NotApplied()
    {
        var tagRules = new[]
        {
            new DurationTagRule { TenantId = Guid.NewGuid(), ResourceType = "OtherType", Tag = "Anxious", AdditionalMinutes = 15 },
        };

        var result = _estimator.Estimate(
            new DurationEstimationInput { ResourceType = "TestType", AttributeTags = ["Anxious"] },
            sizeBrackets: [],
            tagRules,
            defaultBaseMinutes: 60);

        Assert.Equal(0, result.BufferMinutes);
    }
}
