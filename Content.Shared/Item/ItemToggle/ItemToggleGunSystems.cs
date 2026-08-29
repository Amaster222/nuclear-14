using Content.Shared.Hands;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Shared.Item.ItemToggle;

/// <summary>
/// This handles toggling guns on and off for the purposes of changing their stats during different active states
/// </summary>
public sealed class ItemToggleGunSystems : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;


    public override void Initialize()
    {
        SubscribeLocalEvent<ItemToggleGunComponent, ItemToggledEvent>(ActivateGun);
        SubscribeLocalEvent<ItemToggleGunComponent, GunRefreshModifiersEvent>(ActiveFireRate);
        SubscribeLocalEvent<ItemToggleGunComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(ActiveSpeedModifier);
    }


    /// <summary>
    /// Calls upon refreshes for gun stats and movement speed when the gun is toggled
    /// </summary>
    private void ActivateGun(EntityUid uid, ItemToggleGunComponent component, ref ItemToggledEvent args)
    {
        _gun.RefreshModifiers(uid);
        if (TryComp<TransformComponent>(uid, out var xform) && xform.ParentUid.IsValid())
        {
            _movementSpeedModifier.RefreshMovementSpeedModifiers(xform.ParentUid);
        }
        Dirty(uid, component);

    }

    /// <summary>
    /// Handles changing the fire rate when the gun is active and inactive
    /// </summary>
    public void ActiveFireRate(Entity<ItemToggleGunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (TryComp<ItemToggleComponent>(ent, out var toggle) && toggle.Activated)
        {
            args.FireRate = ent.Comp.ActivatedFireRate;
        }
        else
        {
            args.FireRate = ent.Comp.InactiveWeaponFireRate;
        }
    }

    /// <summary>
    /// Handles changing user movement speed when the gun is held and active (defaults to base speed when in active)
    /// </summary>
    public void ActiveSpeedModifier(EntityUid uid, ItemToggleGunComponent component, ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>args)
    {
        if (TryComp<ItemToggleComponent>(uid, out var toggle) && toggle.Activated)
        {
            float speedModifierActive = component.ActivatedSpeedModifier;

            args.Args.ModifySpeed(speedModifierActive, speedModifierActive);
        }
    }
}


