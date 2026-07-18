using Eswmp.Work.Models;

namespace Eswmp.Work.Services;

/// <summary>path/code/message shape mirrors the api spec's issues[] entries (§2, §9).</summary>
public record ValidationIssue(string? Path, string Code, string Severity, string Message);

/// <summary>
/// Runs the seven validation categories from docs/api/specs/02-work-requirement-model.md §6 /
/// 02-work-requirement-api.md §9, shared by resolve, POST /validate, and revise so all three
/// apply the identical rule set (api §9: "Structural, semantic, and temporal rules are also
/// enforced as database CHECK constraints, so an invalid requirement cannot be persisted even
/// by a path that bypasses the validator" — this class is the app-level mirror of those
/// constraints, run first so callers get a friendly issues[] instead of a raw DB error).
///
/// Reference-category checks are limited to "is a non-empty code" — CapabilityDefinition,
/// CertificationType, and CapacityDimension are owned by other modules/services (model §1.2)
/// that don't exist yet in this platform, so there is no registry to validate against.
/// Policy-category checks are a deliberate no-op — tenant/organization policy gates belong to
/// Eswmp.Rules, not to this service (CLAUDE.md service boundaries).
/// </summary>
public static class RequirementValidationService
{
    public static List<ValidationIssue> Evaluate(WorkRequirement wr)
    {
        var issues = new List<ValidationIssue>();

        Structural(wr, issues);
        Reference(wr, issues);
        Semantic(wr, issues);
        Temporal(wr, issues);
        Composition(wr, issues);
        CrossRequirement(wr, issues);
        // Policy: intentionally a no-op — see class remarks.

        return issues;
    }

    private static void Structural(WorkRequirement wr, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(wr.WorkType))
        {
            issues.Add(new ValidationIssue("workType", "WORK_TYPE_REQUIRED", "Error", "workType is required."));
        }

        if (wr.ResourceRequirements.Count == 0)
        {
            issues.Add(new ValidationIssue("resourceRequirements", "MISSING_REQUIRED_RESOURCE_ROLE", "Error",
                "At least one resource role requirement is required."));
        }

        if (wr.DurationRequirement is null)
        {
            issues.Add(new ValidationIssue("durationRequirement", "DURATION_REQUIRED", "Error",
                "A duration requirement is required."));
        }

        if (string.IsNullOrWhiteSpace(wr.SourceType) || string.IsNullOrWhiteSpace(wr.SourceId))
        {
            issues.Add(new ValidationIssue("sourceId", "SOURCE_REFERENCE_REQUIRED", "Error",
                "sourceType/sourceId are required."));
        }
    }

    private static void Reference(WorkRequirement wr, List<ValidationIssue> issues)
    {
        foreach (var c in wr.CapabilityRequirements.Where(c => string.IsNullOrWhiteSpace(c.CapabilityCode)))
        {
            _ = c;
            issues.Add(new ValidationIssue("capabilityRequirements", "INVALID_CAPABILITY_CODE", "Error", "capabilityCode is required."));
        }

        foreach (var c in wr.CertificationRequirements.Where(c => string.IsNullOrWhiteSpace(c.CertificationTypeCode)))
        {
            _ = c;
            issues.Add(new ValidationIssue("certificationRequirements", "INVALID_CERTIFICATION_TYPE_CODE", "Error", "certificationTypeCode is required."));
        }

        foreach (var c in wr.CapacityRequirements.Where(c => string.IsNullOrWhiteSpace(c.DimensionCode)))
        {
            _ = c;
            issues.Add(new ValidationIssue("capacityRequirements", "INVALID_CAPACITY_DIMENSION_CODE", "Error", "dimensionCode is required."));
        }

        if (wr.LocationRequirement is { LocationMode: LocationMode.SpecificLocation } loc &&
            string.IsNullOrWhiteSpace(loc.LocationReferenceId))
        {
            issues.Add(new ValidationIssue("locationRequirement", "INVALID_LOCATION_REFERENCE", "Error",
                "locationReferenceId is required when locationMode is SpecificLocation."));
        }
    }

    private static void Semantic(WorkRequirement wr, List<ValidationIssue> issues)
    {
        foreach (var role in wr.ResourceRequirements)
        {
            if (role.MinimumQuantity <= 0)
            {
                issues.Add(new ValidationIssue("resourceRequirements", "INVALID_QUANTITY", "Error",
                    $"Role '{role.RoleCode}': minimumQuantity must be positive."));
            }
            if (role.MaximumQuantity is not null && role.MaximumQuantity < role.MinimumQuantity)
            {
                issues.Add(new ValidationIssue("resourceRequirements", "INVALID_QUANTITY_RANGE", "Error",
                    $"Role '{role.RoleCode}': maximumQuantity must be >= minimumQuantity."));
            }
        }

        foreach (var capacity in wr.CapacityRequirements)
        {
            if (capacity.Quantity <= 0)
            {
                issues.Add(new ValidationIssue("capacityRequirements", "INVALID_QUANTITY", "Error",
                    $"Dimension '{capacity.DimensionCode}': quantity must be positive."));
            }
        }

        if (wr.DurationRequirement is { } duration)
        {
            if (duration.DurationType == DurationType.Fixed && duration.EstimatedDurationMinutes is null or <= 0)
            {
                issues.Add(new ValidationIssue("durationRequirement", "INVALID_DURATION", "Error",
                    "estimatedDurationMinutes must be positive when durationType is Fixed."));
            }

            if (duration.MinimumDurationMinutes is not null && duration.MaximumDurationMinutes is not null &&
                duration.MinimumDurationMinutes > duration.MaximumDurationMinutes)
            {
                issues.Add(new ValidationIssue("durationRequirement", "INVALID_DURATION_RANGE", "Error",
                    "minimumDurationMinutes must be <= maximumDurationMinutes."));
            }
        }
    }

    private static void Temporal(WorkRequirement wr, List<ValidationIssue> issues)
    {
        if (wr.TimeRequirement is { } time)
        {
            if (time.EarliestStart is not null && time.LatestStart is not null && time.EarliestStart > time.LatestStart)
            {
                issues.Add(new ValidationIssue("timeRequirement", "INVALID_TIME_ORDERING", "Error",
                    "latestStart must not be before earliestStart."));
            }

            if (time.EarliestStart is not null && time.Deadline is not null && time.Deadline < time.EarliestStart)
            {
                issues.Add(new ValidationIssue("timeRequirement", "INVALID_DEADLINE", "Error",
                    "deadline must not be before earliestStart."));
            }

            foreach (var cert in wr.CertificationRequirements.Where(c => c.MustBeValidThrough is not null))
            {
                var deadline = time.LatestFinish ?? time.Deadline ?? time.FixedEnd;
                if (deadline is not null && cert.MustBeValidThrough < deadline)
                {
                    issues.Add(new ValidationIssue("certificationRequirements", "CERTIFICATION_EXPIRES_BEFORE_WORK", "Error",
                        $"Certification '{cert.CertificationTypeCode}' must remain valid through the work period."));
                }
            }
        }
    }

    /// <summary>Mobile execution implies a vehicle role — a warning, not a hard error, absent a
    /// tenant policy that escalates it (model §6: "warning or error by policy").</summary>
    private static void Composition(WorkRequirement wr, List<ValidationIssue> issues)
    {
        if (wr.ExecutionRequirement is { ExecutionMode: ExecutionMode.Mobile } &&
            !wr.ResourceRequirements.Any(r => r.ResourceCategory == ResourceCategory.Vehicle))
        {
            issues.Add(new ValidationIssue("resourceRequirements", "MOBILE_REQUIRES_VEHICLE_ROLE", "Warning",
                "Mobile execution typically requires a Vehicle resource role."));
        }
    }

    private static void CrossRequirement(WorkRequirement wr, List<ValidationIssue> issues)
    {
        var roleIds = wr.ResourceRequirements.Select(r => r.Id).ToHashSet();

        foreach (var capacity in wr.CapacityRequirements.Where(c => c.ResourceRoleRequirementId is not null))
        {
            if (!roleIds.Contains(capacity.ResourceRoleRequirementId!.Value))
            {
                issues.Add(new ValidationIssue("capacityRequirements", "CAPACITY_ROLE_NOT_FOUND", "Error",
                    $"Capacity requirement '{capacity.DimensionCode}' references a resource role that does not exist on this work requirement."));
            }
        }

        foreach (var capability in wr.CapabilityRequirements.Where(c => c.ResourceRoleRequirementId is not null))
        {
            if (!roleIds.Contains(capability.ResourceRoleRequirementId!.Value))
            {
                issues.Add(new ValidationIssue("capabilityRequirements", "CAPABILITY_ROLE_NOT_FOUND", "Error",
                    $"Capability requirement '{capability.CapabilityCode}' references a resource role that does not exist on this work requirement."));
            }
        }

        foreach (var certification in wr.CertificationRequirements.Where(c => c.ResourceRoleRequirementId is not null))
        {
            if (!roleIds.Contains(certification.ResourceRoleRequirementId!.Value))
            {
                issues.Add(new ValidationIssue("certificationRequirements", "CERTIFICATION_ROLE_NOT_FOUND", "Error",
                    $"Certification requirement '{certification.CertificationTypeCode}' references a resource role that does not exist on this work requirement."));
            }
        }
    }
}
