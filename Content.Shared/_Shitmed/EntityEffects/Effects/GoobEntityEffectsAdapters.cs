using System.Linq;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Pain.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Components;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._Shitmed.EntityEffects.Effects;

public sealed partial class AdjustBoneDamage : EntityEffect
{
    [DataField(required: true)] public FixedPoint2 Amount = default!;
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager systems) => Loc.GetString("reagent-effect-guidebook-adjust-bone-damage", ("amount", Amount));
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<BodyComponent>(args.TargetEntity, out var body)) return;
        var bodySystem = args.EntityManager.System<SharedBodySystem>();
        var trauma = args.EntityManager.System<TraumaSystem>();
        var parts = bodySystem.GetBodyChildrenWithComponent<WoundableComponent>(args.TargetEntity).ToList();
        if (parts.Count == 0) return;
        foreach (var (_, _, woundable) in parts)
        {
            var bone = woundable.Bone.ContainedEntities.FirstOrNull();
            if (bone != null && args.EntityManager.TryGetComponent<BoneComponent>(bone, out var comp)) trauma.ApplyDamageToBone(bone.Value, Amount / parts.Count, comp);
        }
    }
}

public sealed partial class AdjustConsciousness : EntityEffect
{
    [DataField(required: true)] public FixedPoint2 Amount = default!;
    [DataField(required: true)] public TimeSpan Time = default!;
    [DataField] public string Identifier = "ConsciousnessModifier";
    [DataField] public bool AllowNewModifiers = true;
    [DataField] public ConsciousnessModType ModifierType = ConsciousnessModType.Generic;
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager systems) => Loc.GetString("reagent-effect-guidebook-adjust-consciousness");
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<BodyComponent>(args.TargetEntity, out var body)) return;
        var system = args.EntityManager.System<ConsciousnessSystem>();
        if (!system.TryGetNerveSystem(args.TargetEntity, out var nerves)) return;
        if (!system.EditConsciousnessModifier(args.TargetEntity, nerves.Value.Owner, Amount, Identifier, Time) && AllowNewModifiers)
            system.AddConsciousnessModifier(args.TargetEntity, nerves.Value.Owner, Amount, Identifier, ModifierType, Time);
    }
}

public sealed partial class AdjustPainFeels : EntityEffect
{
    [DataField(required: true)] public FixedPoint2 Amount = default!;
    [DataField] public string ModifierIdentifier = "PainSuppressant";
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager systems) => Loc.GetString("reagent-effect-guidebook-suppress-pain", ("chance", Probability));
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<BodyComponent>(args.TargetEntity, out var body)) return;
        var consciousness = args.EntityManager.System<ConsciousnessSystem>();
        if (!consciousness.TryGetNerveSystem(args.TargetEntity, out var nerves)) return;
        var pain = args.EntityManager.System<PainSystem>();
        var bodySystem = args.EntityManager.System<SharedBodySystem>();
        var random = IoCManager.Resolve<IRobustRandom>();
        foreach (var (part, _) in bodySystem.GetBodyChildren(args.TargetEntity))
        {
            var amount = random.Prob(0.3f) ? Amount : -Amount;
            if (pain.TryGetPainFeelsModifier(part, nerves.Value.Owner, ModifierIdentifier, out _)) pain.TryChangePainFeelsModifier(nerves.Value.Owner, ModifierIdentifier, part, amount);
            else pain.TryAddPainFeelsModifier(nerves.Value.Owner, ModifierIdentifier, part, amount);
        }
    }
}

public sealed partial class SuppressPain : EntityEffect
{
    [DataField(required: true)] public FixedPoint2 Amount = default!;
    [DataField(required: true)] public TimeSpan Time = default!;
    [DataField] public string ModifierIdentifier = "PainSuppressant";
    [DataField] public string OrganCategory = "Chest";
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager systems) => Loc.GetString("reagent-effect-guidebook-suppress-pain");
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<BodyComponent>(args.TargetEntity, out var body)) return;
        var consciousness = args.EntityManager.System<ConsciousnessSystem>();
        if (!consciousness.TryGetNerveSystem(args.TargetEntity, out var nerves)) return;
        var bodySystem = args.EntityManager.System<SharedBodySystem>();
        var part = bodySystem.GetBodyChildren(args.TargetEntity).FirstOrNull(x => x.Component.PartType.ToString().Equals(OrganCategory, StringComparison.OrdinalIgnoreCase));
        if (part == null) return;
        var pain = args.EntityManager.System<PainSystem>();
        if (pain.TryGetPainModifier(nerves.Value.Owner, part.Value.Id, ModifierIdentifier, out var modifier)) pain.TryChangePainModifier(nerves.Value.Owner, part.Value.Id, ModifierIdentifier, modifier.Value.Change - Amount, time: Time);
        else pain.TryAddPainModifier(nerves.Value.Owner, part.Value.Id, ModifierIdentifier, -Amount, time: Time);
    }
}
