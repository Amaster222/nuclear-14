using Content.Shared.Hands;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;

namespace Content.Shared.Item.ItemToggle;

/// <summary>
/// This handles toggling guns on and off for the purposes of changing their stats during different active states
/// </summary>
public sealed partial class MinigunToggleSystem : EntitySystem
{
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private MovementSpeedModifierSystem _move = default!;
    public override void Initialize()
    {

        SubscribeLocalEvent<MinigunToggleComponent, UseInHandEvent>(OnUseTryActivate, before: [typeof(SharedGunSystem)]);
        SubscribeLocalEvent<MinigunToggleComponent, GunRefreshModifiersEvent>(ActiveFireRate);
        SubscribeLocalEvent<MinigunToggleComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(ActiveSpeedModifier);
    }
    // TODO: rework
    private void OnUseTryActBallistic(EntityUid uid, ChamberMagazineAmmoProviderComponent compBallistic, UseInHandEvent args)
    {

        var closedBolt = compBallistic.BoltClosed;
        if (closedBolt != true || _gun.GetChamberEntity(uid) is null)
        {
            _toggle.TryDeactivate(uid);
        }
        else
        {
            _toggle.Toggle(uid);
            args.Handled = true;
        }
    }
    // TODO: rework
    public void OnUseTryActivate(EntityUid uid, MinigunToggleComponent comp, UseInHandEvent args)
    {
        if (TryComp<ChamberMagazineAmmoProviderComponent>(uid, out var compBallistic))
        {
            OnUseTryActBallistic(uid, compBallistic, args);
        }
        else
        {
            _toggle.Toggle(uid);
            args.Handled = true;
        }

        _gun.RefreshModifiers(uid);
        _move.RefreshMovementSpeedModifiers(args.User);
    }

    /// <summary>
    /// Handles changing the fire rate when the gun is active and inactive
    /// </summary>
    public void ActiveFireRate(Entity<MinigunToggleComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var comp = Comp<ItemToggleComponent>(ent.Owner);
        args.FireRate = comp.Activated ? ent.Comp.ActivatedFireRate : ent.Comp.InactiveWeaponFireRate;

    }

    /// <summary>
    /// Handles changing user movement speed when the gun is held and active (defaults to base speed when in active)
    /// </summary>
    public void ActiveSpeedModifier(EntityUid uid, MinigunToggleComponent comp, ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var active = Comp<ItemToggleComponent>(uid).Activated;
        float speedMod = active ? comp.ActivatedSpeedModifier : 1f;
        args.Args.ModifySpeed(speedMod, speedMod, true);
    }
}


