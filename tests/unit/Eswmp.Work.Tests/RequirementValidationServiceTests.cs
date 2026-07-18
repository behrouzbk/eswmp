using Eswmp.Work.Models;
using Eswmp.Work.Services;
using Xunit;

namespace Eswmp.Work.Tests;

public class RequirementValidationServiceTests
{
    private static WorkRequirement ValidWorkRequirement()
    {
        var wr = new WorkRequirement
        {
            TenantId = Guid.NewGuid(),
            SourceType = "Demand",
            SourceId = "demand-1",
            WorkType = "DOG_WALKING",
        };
        var role = new ResourceRoleRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            RoleCode = "DOG_WALKER",
            ResourceCategory = ResourceCategory.Person,
            MinimumQuantity = 1,
            MaximumQuantity = 1,
        };
        wr.ResourceRequirements.Add(role);
        wr.DurationRequirement = new DurationRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            DurationType = DurationType.Fixed,
            EstimatedDurationMinutes = 60,
        };
        wr.CapacityRequirements.Add(new CapacityRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            ResourceRoleRequirementId = role.Id,
            DimensionCode = "PET_COUNT",
            Quantity = 1,
        });
        return wr;
    }

    [Fact]
    public void Evaluate_ValidRequirement_HasNoErrors()
    {
        var wr = ValidWorkRequirement();

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.DoesNotContain(issues, i => i.Severity == "Error");
    }

    [Fact]
    public void Structural_MissingResourceRoles_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.ResourceRequirements.Clear();

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "MISSING_REQUIRED_RESOURCE_ROLE" && i.Severity == "Error");
    }

    [Fact]
    public void Structural_MissingDuration_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.DurationRequirement = null;

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "DURATION_REQUIRED" && i.Severity == "Error");
    }

    [Fact]
    public void Reference_BlankCapabilityCode_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.CapabilityRequirements.Add(new CapabilityRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            CapabilityCode = "",
        });

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "INVALID_CAPABILITY_CODE");
    }

    [Fact]
    public void Semantic_MaximumBelowMinimumQuantity_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.ResourceRequirements[0].MaximumQuantity = null;
        wr.ResourceRequirements[0].MinimumQuantity = 3;
        wr.ResourceRequirements[0].MaximumQuantity = 1;

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "INVALID_QUANTITY_RANGE");
    }

    [Fact]
    public void Semantic_NonPositiveCapacityQuantity_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.CapacityRequirements[0].Quantity = 0;

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "INVALID_QUANTITY" && i.Path == "capacityRequirements");
    }

    [Fact]
    public void Temporal_LatestStartBeforeEarliestStart_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.TimeRequirement = new TimeRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            TimeConstraintType = TimeConstraintType.Window,
            EarliestStart = DateTimeOffset.UtcNow.AddHours(2),
            LatestStart = DateTimeOffset.UtcNow,
        };

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "INVALID_TIME_ORDERING");
    }

    [Fact]
    public void Temporal_CertificationExpiresBeforeWorkPeriod_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.TimeRequirement = new TimeRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            TimeConstraintType = TimeConstraintType.Window,
            EarliestStart = DateTimeOffset.UtcNow,
            LatestFinish = DateTimeOffset.UtcNow.AddDays(30),
        };
        wr.CertificationRequirements.Add(new CertificationRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            CertificationTypeCode = "ANIMAL_FIRST_AID",
            MustBeValidThrough = DateTimeOffset.UtcNow.AddDays(1),
        });

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "CERTIFICATION_EXPIRES_BEFORE_WORK");
    }

    [Fact]
    public void Composition_MobileExecutionWithoutVehicleRole_IsWarning()
    {
        var wr = ValidWorkRequirement();
        wr.ExecutionRequirement = new ExecutionRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            ExecutionMode = ExecutionMode.Mobile,
        };

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "MOBILE_REQUIRES_VEHICLE_ROLE" && i.Severity == "Warning");
        Assert.DoesNotContain(issues, i => i.Code == "MOBILE_REQUIRES_VEHICLE_ROLE" && i.Severity == "Error");
    }

    [Fact]
    public void CrossRequirement_CapacityReferencesUnknownRole_IsError()
    {
        var wr = ValidWorkRequirement();
        wr.CapacityRequirements.Add(new CapacityRequirement
        {
            TenantId = wr.TenantId,
            WorkRequirementId = wr.Id,
            ResourceRoleRequirementId = Guid.NewGuid(),
            DimensionCode = "PET_COUNT",
            Quantity = 1,
        });

        var issues = RequirementValidationService.Evaluate(wr);

        Assert.Contains(issues, i => i.Code == "CAPACITY_ROLE_NOT_FOUND");
    }
}
