using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    // needed for server system
    protected virtual void InitializeCartridge()
    {
        SubscribeLocalEvent<CartridgeAmmoComponent, TakeAmmoEvent>(OnTakeAmmo);
    }
    private void OnTakeAmmo(EntityUid uid, CartridgeAmmoComponent giverComp, TakeAmmoEvent args)
    {
        args.Ammo.Add((uid, EnsureShootable(uid)));
        Dirty(uid, giverComp);
    }
}
