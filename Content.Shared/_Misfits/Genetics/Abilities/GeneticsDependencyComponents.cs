// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage.Systems;
using Content.Shared.Slippery;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;

namespace Content.Shared._Misfits.Genetics.Abilities;

[RegisterComponent, NetworkedComponent]
public sealed partial class SpecialLowTempImmunityComponent : Component
{
    public override bool SessionSpecific => true;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SpecialHighTempImmunityComponent : Component
{
    public override bool SessionSpecific => true;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class FireImmunityComponent : Component
{
    public override bool SessionSpecific => true;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class WeatherImmuneComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class DeafComponent : Component;

/// <summary>
/// Genetics-compatible subset of Trauma's hulk state. It preserves the mutation's stamina and slip immunity
/// and its large unarmed/melee damage multiplier without importing Trauma's wizard and laser-eye subsystems.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HulkComponent : Component
{
    [DataField] public float? Duration;
    [DataField] public bool LaserEyes = true;
    [DataField] public SoundSpecifier? SoundGunshot;
    [DataField] public EntProtoId? ShotProto;
    [DataField] public float FistDamageMultiplier = 7f;
    [DataField] public float MaxBonusFistDamage = 50f;
    [DataField] public Color SkinColor = Color.FromHex("#4EDB53");
    [DataField] public Color EyeColor = Color.FromHex("#910C17");
}

public sealed class HulkSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HulkComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
        SubscribeLocalEvent<HulkComponent, SlipAttemptEvent>(OnSlipAttempt);
        SubscribeLocalEvent<HulkComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private static void OnBeforeStaminaDamage(Entity<HulkComponent> ent, ref BeforeStaminaDamageEvent args)
        => args.Cancelled = true;

    private static void OnSlipAttempt(Entity<HulkComponent> ent, ref SlipAttemptEvent args)
        => args.Cancel();

    private static void OnGetMeleeDamage(Entity<HulkComponent> ent, ref GetMeleeDamageEvent args)
    {
        var bonus = new Content.Shared.Damage.DamageSpecifier(args.Damage);
        bonus *= ent.Comp.FistDamageMultiplier;
        var total = bonus.GetTotal().Float();
        if (total > ent.Comp.MaxBonusFistDamage)
            bonus *= ent.Comp.MaxBonusFistDamage / total;
        args.Damage += bonus;
    }
}
