using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eswmp.Work.Models;

namespace Eswmp.Work.Services;

/// <summary>Thrown when inputs/DefinitionJson exceed the bounds in api spec §10.5 — callers map this to 413.</summary>
public class JsonBoundsExceededException(string message) : Exception(message);

/// <summary>
/// jsonb bounds (api spec §10.5) — inputs and DefinitionJson are free-form, "the natural
/// abuse vector", so both get a byte-size cap and a nesting-depth cap before being parsed
/// into anything else.
/// </summary>
public static class JsonGuard
{
    public const int MaxBytes = 65536;
    public const int MaxDepth = 12;

    public static void Validate(string json, string fieldName)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            throw new JsonBoundsExceededException($"{fieldName} exceeds the maximum size of {MaxBytes} bytes.");
        }

        using var doc = JsonDocument.Parse(json);
        if (MeasureDepth(doc.RootElement, 1) > MaxDepth)
        {
            throw new JsonBoundsExceededException($"{fieldName} exceeds the maximum nesting depth of {MaxDepth}.");
        }
    }

    private static int MeasureDepth(JsonElement element, int current)
    {
        if (current > MaxDepth)
        {
            return current;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Select(p => MeasureDepth(p.Value, current + 1)).DefaultIfEmpty(current).Max(),
            JsonValueKind.Array => element.EnumerateArray().Select(e => MeasureDepth(e, current + 1)).DefaultIfEmpty(current).Max(),
            _ => current,
        };
    }
}

/// <summary>
/// Materializes a RequirementTemplateVersion's frozen DefinitionJson (+ a bounded, fixed
/// overlay from the caller's resolve `inputs`) into a live WorkRequirement's child entity
/// graph, and the reverse — reading that graph back out as the wire-shape ResolvedRequirements
/// contract (api spec §3.2). Everything here is a fixed structural mapping — no expression
/// evaluation of any kind is applied to tenant-supplied input, per api spec §10.4: "an
/// arbitrary expression evaluator in a multi-tenant service is a remote-code-execution surface."
/// </summary>
public static class RequirementResolutionService
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Npgsql only accepts DateTimeOffset with Offset=0 for a `timestamp with time zone`
    /// column — every DateTimeOffset parsed from caller-supplied JSON (which may carry any
    /// offset) must be normalized before it reaches a TimeRequirement entity.
    /// </summary>
    public static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();

    /// <summary>Builds the child entity graph on <paramref name="wr"/> from a template's frozen definitions.</summary>
    public static void ApplyDefinitions(WorkRequirement wr, RequirementSetDto defs, Guid tenantId)
    {
        var roleByCode = new Dictionary<string, ResourceRoleRequirement>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in defs.ResourceRequirements ?? [])
        {
            var role = new ResourceRoleRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                RoleCode = r.RoleCode,
                ResourceCategory = r.ResourceCategory,
                MinimumQuantity = r.MinimumQuantity,
                MaximumQuantity = r.MaximumQuantity,
                Required = r.Required,
                SelectionMode = r.SelectionMode,
                SameResourceRequired = r.SameResourceRequired,
                Sequence = r.Sequence,
            };
            wr.ResourceRequirements.Add(role);
            roleByCode[r.RoleCode] = role;
        }

        Guid? RoleId(string? roleCode) =>
            roleCode is not null && roleByCode.TryGetValue(roleCode, out var role) ? role.Id : null;

        foreach (var c in defs.CapabilityRequirements ?? [])
        {
            wr.CapabilityRequirements.Add(new CapabilityRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                ResourceRoleRequirementId = RoleId(c.RoleCode),
                CapabilityCode = c.CapabilityCode,
                Level = c.Level,
                MinimumExperience = c.MinimumExperience,
                Mandatory = c.Mandatory,
                Scope = c.Scope,
            });
        }

        foreach (var c in defs.CertificationRequirements ?? [])
        {
            wr.CertificationRequirements.Add(new CertificationRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                ResourceRoleRequirementId = RoleId(c.RoleCode),
                CertificationTypeCode = c.CertificationTypeCode,
                Mandatory = c.Mandatory,
                MustBeValidThrough = ToUtc(c.MustBeValidThrough),
                VerificationLevel = c.VerificationLevel,
            });
        }

        foreach (var c in defs.CapacityRequirements ?? [])
        {
            wr.CapacityRequirements.Add(new CapacityRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                ResourceRoleRequirementId = RoleId(c.RoleCode),
                DimensionCode = c.DimensionCode,
                Quantity = c.Quantity,
                Unit = c.Unit,
                AggregationScope = c.AggregationScope,
                Mandatory = c.Mandatory,
            });
        }

        if (defs.DurationRequirement is { } d)
        {
            wr.DurationRequirement = new DurationRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                DurationType = d.DurationType,
                EstimatedDurationMinutes = d.EstimatedDurationMinutes,
                MinimumDurationMinutes = d.MinimumDurationMinutes,
                MaximumDurationMinutes = d.MaximumDurationMinutes,
                SetupDurationMinutes = d.SetupDurationMinutes,
                CleanupDurationMinutes = d.CleanupDurationMinutes,
            };
        }

        if (defs.TimeRequirement is { } t)
        {
            wr.TimeRequirement = new TimeRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                TimeConstraintType = t.TimeConstraintType,
                EarliestStart = ToUtc(t.EarliestStart),
                LatestStart = ToUtc(t.LatestStart),
                EarliestFinish = ToUtc(t.EarliestFinish),
                LatestFinish = ToUtc(t.LatestFinish),
                FixedStart = ToUtc(t.FixedStart),
                FixedEnd = ToUtc(t.FixedEnd),
                Deadline = ToUtc(t.Deadline),
                Timezone = t.Timezone,
            };
        }

        if (defs.LocationRequirement is { } loc)
        {
            wr.LocationRequirement = new LocationRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                LocationMode = loc.LocationMode,
                LocationReferenceType = loc.LocationReferenceType,
                LocationReferenceId = loc.LocationReferenceId,
                Latitude = loc.Latitude,
                Longitude = loc.Longitude,
                ServiceRadius = loc.ServiceRadius,
                LocationFlexibility = loc.LocationFlexibility,
            };
        }

        if (defs.ExecutionRequirement is { } exec)
        {
            wr.ExecutionRequirement = new ExecutionRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                ExecutionMode = exec.ExecutionMode,
            };
        }

        if (defs.TravelRequirement is { } travel)
        {
            wr.TravelRequirement = new TravelRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                TravelRequired = travel.TravelRequired,
                OriginMode = travel.OriginMode,
                DestinationMode = travel.DestinationMode,
                MaximumTravelTimeMinutes = travel.MaximumTravelTimeMinutes,
                MaximumTravelDistance = travel.MaximumTravelDistance,
                TravelTimeIncludedInWork = travel.TravelTimeIncludedInWork,
            };
        }

        foreach (var b in defs.BufferRequirements ?? [])
        {
            wr.BufferRequirements.Add(new BufferRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                BufferType = b.BufferType,
                DurationMinutes = b.DurationMinutes,
                AppliesToRole = b.AppliesToRole,
                HardConstraint = b.HardConstraint,
            });
        }

        foreach (var dep in defs.DependencyRequirements ?? [])
        {
            wr.DependencyRequirements.Add(new DependencyRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                DependencyType = dep.DependencyType,
                DependsOnReferenceType = dep.DependsOnReferenceType,
                DependsOnReferenceId = dep.DependsOnReferenceId,
                LagMinutes = dep.LagMinutes,
                HardConstraint = dep.HardConstraint,
            });
        }

        foreach (var con in defs.Constraints ?? [])
        {
            wr.Constraints.Add(new RequirementConstraint
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                ConstraintType = con.ConstraintType,
                Scope = con.Scope,
                Operator = con.Operator,
                Value = con.Value,
                HardConstraint = con.HardConstraint,
                Reason = con.Reason,
            });
        }

        foreach (var pref in defs.Preferences ?? [])
        {
            wr.Preferences.Add(new RequirementPreference
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                PreferenceType = pref.PreferenceType,
                Value = pref.Value,
                Weight = pref.Weight,
                Source = pref.Source,
            });
        }
    }

    /// <summary>
    /// Overlays a fixed, bounded set of fields from the resolve request's `inputs` onto an
    /// already-materialized WorkRequirement (api spec §5 example: petCount → a matching
    /// CapacityRequirement's Quantity; requestedWindow → TimeRequirement; locationReferenceId
    /// → LocationRequirement). Every mapping here is a named, structural field assignment —
    /// there is no generic/dynamic code path that could be steered by input shape alone.
    /// </summary>
    public static void ApplyInputs(WorkRequirement wr, JsonElement? inputs, Guid tenantId)
    {
        if (inputs is not { ValueKind: JsonValueKind.Object } root)
        {
            return;
        }

        if (root.TryGetProperty("requestedWindow", out var window) && window.ValueKind == JsonValueKind.Object)
        {
            wr.TimeRequirement ??= new TimeRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                TimeConstraintType = TimeConstraintType.Window,
            };

            if (window.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(start.GetString(), out var startValue))
            {
                wr.TimeRequirement.EarliestStart = ToUtc(startValue);
            }

            if (window.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(end.GetString(), out var endValue))
            {
                wr.TimeRequirement.LatestFinish = ToUtc(endValue);
            }
        }

        if (root.TryGetProperty("locationReferenceId", out var locationRef) && locationRef.ValueKind == JsonValueKind.String)
        {
            wr.LocationRequirement ??= new LocationRequirement
            {
                TenantId = tenantId,
                WorkRequirementId = wr.Id,
                LocationMode = LocationMode.CustomerLocation,
            };
            wr.LocationRequirement.LocationReferenceId = locationRef.GetString();
        }

        foreach (var name in new[] { "workCategory", "serviceMode", "complexityLevel" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                switch (name)
                {
                    case "workCategory": wr.WorkCategory = value.GetString(); break;
                    case "serviceMode": wr.ServiceMode = value.GetString(); break;
                    case "complexityLevel": wr.ComplexityLevel = value.GetString(); break;
                }
            }
        }

        // Numeric top-level scalars overlay a matching capacity dimension by convention:
        // camelCase input name -> UPPER_SNAKE_CASE dimension code (petCount -> PET_COUNT).
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var dimensionCode = ToUpperSnakeCase(property.Name);
            var match = wr.CapacityRequirements.FirstOrDefault(c => string.Equals(c.DimensionCode, dimensionCode, StringComparison.OrdinalIgnoreCase));
            if (match is not null && property.Value.TryGetDecimal(out var quantity))
            {
                match.Quantity = quantity;
            }
        }
    }

    /// <summary>The reverse of ApplyDefinitions — reads a loaded WorkRequirement graph back out
    /// as the wire-shape ResolvedRequirements contract for GET .../resolved.</summary>
    public static RequirementSetDto ToRequirementSet(WorkRequirement wr)
    {
        var roleCodeById = wr.ResourceRequirements.ToDictionary(r => r.Id, r => r.RoleCode);
        string? RoleCode(Guid? roleId) => roleId is { } id && roleCodeById.TryGetValue(id, out var code) ? code : null;

        return new RequirementSetDto(
            ResourceRequirements:
            [.. wr.ResourceRequirements.Select(r => new ResourceRoleRequirementDto(
                r.RoleCode, r.ResourceCategory, r.MinimumQuantity, r.MaximumQuantity, r.Required, r.SelectionMode, r.SameResourceRequired, r.Sequence))],
            DurationRequirement: wr.DurationRequirement is { } d
                ? new DurationRequirementDto(d.DurationType, d.EstimatedDurationMinutes, d.MinimumDurationMinutes, d.MaximumDurationMinutes, d.SetupDurationMinutes, d.CleanupDurationMinutes)
                : throw new InvalidOperationException("A resolved WorkRequirement must always carry a DurationRequirement."),
            CapabilityRequirements: wr.CapabilityRequirements.Count == 0 ? null :
                [.. wr.CapabilityRequirements.Select(c => new CapabilityRequirementDto(c.CapabilityCode, RoleCode(c.ResourceRoleRequirementId), c.Level, c.MinimumExperience, c.Mandatory, c.Scope))],
            CertificationRequirements: wr.CertificationRequirements.Count == 0 ? null :
                [.. wr.CertificationRequirements.Select(c => new CertificationRequirementDto(c.CertificationTypeCode, RoleCode(c.ResourceRoleRequirementId), c.Mandatory, c.MustBeValidThrough, c.VerificationLevel))],
            CapacityRequirements: wr.CapacityRequirements.Count == 0 ? null :
                [.. wr.CapacityRequirements.Select(c => new CapacityRequirementDto(c.DimensionCode, c.Quantity, RoleCode(c.ResourceRoleRequirementId), c.Unit, c.AggregationScope, c.Mandatory))],
            TimeRequirement: wr.TimeRequirement is { } t
                ? new TimeRequirementDto(t.TimeConstraintType, t.EarliestStart, t.LatestStart, t.EarliestFinish, t.LatestFinish, t.FixedStart, t.FixedEnd, t.Deadline, t.Timezone)
                : null,
            LocationRequirement: wr.LocationRequirement is { } loc
                ? new LocationRequirementDto(loc.LocationMode, loc.LocationReferenceType, loc.LocationReferenceId, loc.Latitude, loc.Longitude, loc.ServiceRadius, loc.LocationFlexibility)
                : null,
            ExecutionRequirement: wr.ExecutionRequirement is { } exec ? new ExecutionRequirementDto(exec.ExecutionMode) : null,
            TravelRequirement: wr.TravelRequirement is { } travel
                ? new TravelRequirementDto(travel.TravelRequired, travel.OriginMode, travel.DestinationMode, travel.MaximumTravelTimeMinutes, travel.MaximumTravelDistance, travel.TravelTimeIncludedInWork)
                : null,
            BufferRequirements: wr.BufferRequirements.Count == 0 ? null :
                [.. wr.BufferRequirements.Select(b => new BufferRequirementDto(b.BufferType, b.DurationMinutes, b.AppliesToRole, b.HardConstraint))],
            DependencyRequirements: wr.DependencyRequirements.Count == 0 ? null :
                [.. wr.DependencyRequirements.Select(d => new DependencyRequirementDto(d.DependencyType, d.DependsOnReferenceType, d.DependsOnReferenceId, d.LagMinutes, d.HardConstraint))],
            Constraints: wr.Constraints.Count == 0 ? null :
                [.. wr.Constraints.Select(c => new ConstraintDto(c.ConstraintType, c.Scope, c.Operator, c.Value, c.HardConstraint, c.Reason))],
            Preferences: wr.Preferences.Count == 0 ? null :
                [.. wr.Preferences.Select(p => new PreferenceDto(p.PreferenceType, p.Value, p.Weight, p.Source))]);
    }

    private static string ToUpperSnakeCase(string camelCase)
    {
        var sb = new StringBuilder();
        foreach (var ch in camelCase)
        {
            if (char.IsUpper(ch) && sb.Length > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }
}
