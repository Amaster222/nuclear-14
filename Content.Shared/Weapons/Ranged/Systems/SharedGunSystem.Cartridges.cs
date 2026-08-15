using Content.Shared._Misfits.Engraving;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using static Robust.Shared.Utility.SpriteSpecifier;

namespace Content.Shared.Weapons.Ranged.Systems;
// TODO Misfit: reapproach spent cartridges to be clientside only physics entities after/during gun refactor
//              Spent carts will only be local event raised on shooting client
//              or sent to other clients who are in PVS range
//              server will never spawn a physics ent itself.Just send the event to sessions in PVS
//              We dont care about visual sync, and have it so carts delete when exiting PVS
//              Need to fix guncode to make this possible due to messy code

/// <notes> yapping I did typed days ago
/// referring mostly to <see cref="OnCartEjected"/>
/// method is pretty jank in my view, since we might as well just make cartridges a visual effect
/// we waste time checking for cartridges and then stripping them down to be a visual
/// this is done at least twice, since any cartridges networked need to be stripped down again
/// Specifically the fixture comp gets rebuilt and removing the comp itself causes desync issues
/// basics of why that happens is anything with a physics comp
/// is mostly assumed to have a fixture especially if that's what the proto says
///
/// TLDR having an ent proto spawned with physics only to then strip all that down
/// is bad to do and causes issues/overhead
/// <notes/>
public abstract partial class SharedGunSystem
{
    [Dependency] protected private IGameTiming _timing = default!;

    protected virtual void InitializeCartridge()
    {
        // inserting just the cartridge itself into something
        SubscribeLocalEvent<CartridgeAmmoComponent, TakeAmmoEvent>(OnTakeAmmo);
    }

    /// <summary>
    /// "Taking ammo" from a single cartridge. Done just for compatability/standardization
    /// (not a container with cartridges like <see cref="BallisticAmmoProviderComponent"/>)
    /// </summary>
    private void OnTakeAmmo(EntityUid uid, CartridgeAmmoComponent giverComp, TakeAmmoEvent args)
    {
        args.Ammo.Add((uid, EnsureShootable(uid)));
        Dirty(uid, giverComp);
    }

    public virtual void EjectSpentCart(MapCoordinates coord, Angle angle, string? cartProto, ICommonSession? userSession) { }

    [Serializable, NetSerializable]
    public sealed class SpentCartEvent(MapCoordinates coords, Angle angle, string? proto, NetUserId? sender) : EntityEventArgs
    {
        public MapCoordinates Coords = coords;
        public Angle Angle = angle;
        public string? Proto = proto;
        public NetUserId? Sender = sender;
    }




}
