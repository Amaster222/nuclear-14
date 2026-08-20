using Content.Server.Body.Components;
using Content.Server.Body.Systems;

namespace Content.Server._Shitmed.Medical.Wounds;

/// <summary>
/// Compatibility boundary between Shitmed wound bleeding and Misfits' active
/// bloodstream implementation. Wound systems report only their own aggregate
/// contribution here; chemistry, blood solutions and all non-wound bleeding
/// remain owned by the legacy bloodstream until its callers are migrated.
/// </summary>
public sealed class ShitmedBloodstreamBridgeSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _legacyBloodstream = default!;

    private readonly Dictionary<EntityUid, float> _woundBleedContributions = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<BloodstreamComponent, ComponentShutdown>(OnBloodstreamShutdown);
    }

    public void SetWoundBleedContribution(EntityUid body, float contribution, BloodstreamComponent? bloodstream = null)
    {
        if (!Resolve(body, ref bloodstream))
        {
            _woundBleedContributions.Remove(body);
            return;
        }

        contribution = Math.Clamp(contribution, 0f, bloodstream.MaxBleedAmount);
        var previousContribution = _woundBleedContributions.GetValueOrDefault(body);
        var legacyBleed = Math.Max(0f, bloodstream.BleedAmount - previousContribution);
        var targetBleed = Math.Clamp(legacyBleed + contribution, 0f, bloodstream.MaxBleedAmount);

        if (!MathHelper.CloseTo(bloodstream.BleedAmount, targetBleed))
            _legacyBloodstream.TryModifyBleedAmount(body, targetBleed - bloodstream.BleedAmount, bloodstream);

        _woundBleedContributions[body] = targetBleed - legacyBleed;
    }

    private void OnBloodstreamShutdown(Entity<BloodstreamComponent> ent, ref ComponentShutdown args)
    {
        _woundBleedContributions.Remove(ent);
    }
}
