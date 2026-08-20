using Content.Server.Body.Components;
using Content.Shared._Shitmed.CCVar;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Components;
using Content.Shared._Shitmed.Medical.Surgery.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Components;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Systems;
using Content.Shared.Body.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using FixedPoint2 = Content.Goobstation.Maths.FixedPoint.FixedPoint2;

namespace Content.Server._Shitmed.Medical.Wounds;

/// <summary>
/// Bridges Shitmed's wound-level bleeding into Misfits' still-authoritative
/// server bloodstream. This keeps the established chemistry and blood APIs
/// intact while allowing wounds to start, scale, stop, and block healing.
/// </summary>
public sealed class WoundBleedingSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly WoundSystem _wounds = default!;
    [Dependency] private readonly ShitmedBloodstreamBridgeSystem _bloodstreamBridge = default!;

    private TimeSpan _nextUpdate;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        SubscribeLocalEvent<BleedInflicterComponent, WoundAddedEvent>(OnWoundAdded);
        SubscribeLocalEvent<BleedInflicterComponent, WoundSeverityPointChangedEvent>(OnWoundSeverityChanged);
        SubscribeLocalEvent<BleedInflicterComponent, WoundHealAttemptEvent>(OnWoundHealAttempt);
        SubscribeLocalEvent<BleedRemoverComponent, WoundSeverityPointChangedEvent>(OnBleedRemoverSeverityChanged);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var bleedQuery = EntityQueryEnumerator<BleedInflicterComponent>();
        while (bleedQuery.MoveNext(out var wound, out var bleed))
            UpdateWoundBleedState(wound, bleed);

        var bloodstreamQuery = EntityQueryEnumerator<BloodstreamComponent>();
        while (bloodstreamQuery.MoveNext(out var body, out var bloodstream))
            ApplyWoundBleedToBloodstream(body, bloodstream);
    }

    private void OnWoundAdded(Entity<BleedInflicterComponent> ent, ref WoundAddedEvent args)
    {
        if (!CanWoundBleed(ent, ent.Comp)
            || !args.Woundable.CanBleed
            || args.Component.WoundSeverityPoint < ent.Comp.SeverityThreshold)
            return;

        StartBleeding(ent, args.Component.WoundSeverityPoint);
    }

    private void OnWoundSeverityChanged(Entity<BleedInflicterComponent> ent, ref WoundSeverityPointChangedEvent args)
    {
        if (!CanWoundBleed(ent, ent.Comp)
            || !TryComp<WoundableComponent>(args.Component.HoldingWoundable, out var woundable)
            || !woundable.CanBleed
            || args.NewSeverity < ent.Comp.SeverityThreshold
            || args.NewSeverity < args.OldSeverity)
            return;

        StartBleeding(ent, args.NewSeverity, reopening: !ent.Comp.IsBleeding);
    }

    private void OnWoundHealAttempt(Entity<BleedInflicterComponent> ent, ref WoundHealAttemptEvent args)
    {
        if (!args.IgnoreBlockers && ent.Comp.IsBleeding)
            args.Cancelled = true;
    }

    private void OnBleedRemoverSeverityChanged(Entity<BleedRemoverComponent> ent, ref WoundSeverityPointChangedEvent args)
    {
        var delta = args.NewSeverity - args.OldSeverity;
        if (delta < ent.Comp.SeverityThreshold
            || !TryComp<WoundComponent>(ent, out var wound)
            || !TryComp<WoundableComponent>(wound.HoldingWoundable, out var woundable))
            return;

        _wounds.TryHealBleedingWounds(
            wound.HoldingWoundable,
            (-delta * ent.Comp.BleedingRemovalMultiplier).Float(),
            out _,
            woundable);
    }

    private void StartBleeding(Entity<BleedInflicterComponent> ent, FixedPoint2 severity, bool reopening = false)
    {
        ent.Comp.BleedingAmountRaw = severity * _cfg.GetCVar(SurgeryCVars.BleedingSeverityTrade);
        var duration = (severity / _cfg.GetCVar(SurgeryCVars.BleedsScalingTime) * ent.Comp.ScalingSpeed).Float();
        ent.Comp.ScalingStartsAt = _timing.CurTime;
        ent.Comp.ScalingFinishesAt = _timing.CurTime + TimeSpan.FromSeconds(duration);
        ent.Comp.Scaling = FixedPoint2.New(1);

        if (reopening)
            ent.Comp.ScalingLimit += FixedPoint2.New(0.6);

        ent.Comp.IsBleeding = ent.Comp.BleedingAmountRaw > 0;
        Dirty(ent);
    }

    private void UpdateWoundBleedState(EntityUid wound, BleedInflicterComponent bleed)
    {
        var canBleed = CanWoundBleed(wound, bleed) && bleed.BleedingAmount > 0;
        if (canBleed != bleed.IsBleeding)
        {
            bleed.IsBleeding = canBleed;
            Dirty(wound, bleed);
        }

        if (!bleed.IsBleeding || bleed.Scaling >= bleed.ScalingLimit)
            return;

        var duration = bleed.ScalingFinishesAt - bleed.ScalingStartsAt;
        if (duration <= TimeSpan.Zero)
            return;

        var progress = (_timing.CurTime - bleed.ScalingStartsAt).TotalSeconds / duration.TotalSeconds;
        if (progress <= 0)
            return;

        var target = FixedPoint2.New(1) + (bleed.ScalingLimit - FixedPoint2.New(1)) * FixedPoint2.New(Math.Min(progress, 1));
        var scaling = FixedPoint2.Clamp(target, bleed.Scaling, bleed.ScalingLimit);
        if (scaling == bleed.Scaling)
            return;

        bleed.Scaling = scaling;
        Dirty(wound, bleed);
    }

    private void ApplyWoundBleedToBloodstream(EntityUid body, BloodstreamComponent bloodstream)
    {
        var woundBleed = 0f;
        if (_body.TryGetRootPart(body, out var rootPart))
        {
            foreach (var woundable in _wounds.GetAllWoundableChildren(rootPart.Value))
            {
                var partBleed = FixedPoint2.Zero;
                foreach (var wound in _wounds.GetWoundableWounds(woundable, woundable.Comp))
                {
                    if (TryComp<BleedInflicterComponent>(wound, out var bleed) && bleed.IsBleeding)
                        partBleed += bleed.BleedingAmount;
                }

                woundable.Comp.Bleeds = partBleed;
                woundBleed += partBleed.Float();
            }
        }

        woundBleed = Math.Clamp(woundBleed, 0f, bloodstream.MaxBleedAmount);
        _bloodstreamBridge.SetWoundBleedContribution(body, woundBleed, bloodstream);
    }

    private static bool CanWoundBleed(EntityUid wound, BleedInflicterComponent? component = null)
    {
        if (component == null)
            return false;

        var canBleed = true;
        var highestPriority = 0;
        foreach (var (_, modifier) in component.BleedingModifiers)
        {
            if (modifier.Priority <= highestPriority)
                continue;

            highestPriority = modifier.Priority;
            canBleed = modifier.CanBleed;
        }

        return canBleed;
    }
}
