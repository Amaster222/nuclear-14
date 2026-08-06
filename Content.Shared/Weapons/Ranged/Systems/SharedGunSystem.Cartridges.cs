using Content.Shared._Misfits.Engraving;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Ranged.Systems;
// TODO Misfit: refactor spent cartridges to be a visual effect.
//gotta see how game does dynamic animation or have client only spawned spent cart proto

/// <notes> yapping
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

        // Raised events strip comps/physics on ejected spent carts on client/server side
        // isnt ran for clients outside PVS(so carts still in client's broadphase)

        /// already ran predictvely from client GunSystem on<see cref="RequestShootEvent"/> which calls shared code
        /// so ends up running for both server and client
        SubscribeLocalEvent<CartridgeAmmoComponent, EjectSpentCartEvent>(OnCartEjected);

        // networked only to clients.
        SubscribeNetworkEvent<EjectSpentCartEvent>(OnCartEjected);
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


    /// <summary>
    /// Strips down spent cartridges to be a lightweight entity that wont
    /// slowdown physics on the server but still be animated dynamically as if it had physics
    /// Events wont run for clients outside PVS range so dont rely on spent cartridge data being fully synced
    /// <summary/>
    public void OnCartEjected(EntityUid uid, CartridgeAmmoComponent comp, EjectSpentCartEvent args)
    {
#if DEBUG
        // maybe thisll help later to stop event being raised in the first place multiple times for same ent
        if (!TryComp<FixturesComponent>(uid, out var x) || x.FixtureCount == 0)
        {
            Log.Debug($"eventRaised for cartridge:{uid} Doesnt have fixture TickStamp:{_timing.TickStamp}");
        }
        Log.Debug($"Spent Cartridge with NetID: {args.Cartridge} TickStamp:{_timing.TickStamp}");
#endif

        // prediction or something keeps raising the event on same ent
        if (!TryComp<FixturesComponent>(uid, out var fix) || fix.FixtureCount == 0)
        {
            return;
        }

        // #Misfits Tweak - Reduce casing despawn from 5min to 30s to prevent uid buildup during war
        EnsureComp<TimedDespawnComponent>(uid).Lifetime = 30f;
        //  edge case for null angles
        // happens for carts that stay in container when fired(revolvers)
        if (!args.EjectAngle.HasValue)
        {
            _xform.SetLocalPositionRotation(uid, Transform(uid).Coordinates.Position + args.VectRng,
                                                                                                args.AngleRng);
            // removing physicsComp on same tick ent is spawned
            // doesnt cause issues from doing this in any other case
            RemCompDeferred<PhysicsComponent>(uid);
            StripCartComps(uid);
            return;
        }

        DebugTools.Assert(Comp<FixturesComponent>(uid).FixtureCount > 0);
        // these methods below seem to work because they dont switch "CanColide"
        // which causes entities to stop having physics(movement)
        // we still want movement, but we also dont want the cart to be considered for collisions even indirectly

        // Wont be considered for most collisions but still allowed to move
        // also makes physics remove existing contacts
        _sharedPhysics.SetBodyType(uid, BodyType.KinematicController);
        // remove ent from broadphase, deletes fixture too
        _lookup.RemoveFromEntityTree(uid, Transform(uid));
        StripCartCompsShared(uid);


        _xform.SetLocalRotation(uid, args.AngleRng);
        Angle ejectAngle = args.EjectAngle.Value;
        ejectAngle += 3.7f; // 212 degrees; casings should eject slightly to the right and behind of a gun
        ThrowingSystem.TryThrow(uid, ejectAngle.ToVec() + args.VectRng, 5f);
    }

    /// <summary>
    /// Networked wrapper of above for sending event from server to clients
    /// validates netID and comp
    /// Needed for when clients are in PVS range and spent cartridges originate from
    /// a server triggered event(NPCs shooting, autofire ect...)
    /// </summary>
    public void OnCartEjected(EjectSpentCartEvent args)
    {
#if DEBUG
        Log.Debug($"Spent Cartridge from server NetID: {args.Cartridge}");
#endif
        if (TryGetEntity(args.Cartridge, out var uid) && TryComp<CartridgeAmmoComponent>(uid, out var comp))
        {
            OnCartEjected(uid.Value, comp, args);
        }
    }
    # region other stuff

    /// <summary>
    /// Strip comps that both client and server can access
    /// To make cartridge as non-interactable as possible
    /// and prevent comp events/functions from firing and breaking things
    /// </summary>
    public void StripCartCompsShared(EntityUid uid)
    {

        RemComp<ItemComponent>(uid);
        RemComp<JointComponent>(uid);
        RemComp<DamageOnHighSpeedImpactComponent>(uid);
        RemComp<PullableComponent>(uid);
        RemComp<EngravableComponent>(uid);
        RemComp<DamageExaminableComponent>(uid);
        RemComp<MovedByPressureComponent>(uid);
        StripCartComps(uid);
    }

    /// <summary>
    ///  strips client/server exclusive comps
    /// </summary>
    public virtual void StripCartComps(EntityUid uid) { }
    /// <summary>
    /// Event raised in order to strip comps of a uid with a CartridgeAmmoComponent
    /// to make it just a purely visual object with a short despawn time
    /// Meant for spent cartridges
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class EjectSpentCartEvent : EntityEventArgs
    {
        public NetEntity Cartridge;
        public Angle? EjectAngle;
        public Vector2 VectRng;
        public Angle AngleRng;
        public EjectSpentCartEvent(NetEntity cart, Angle? ejectAngle, Vector2 vectRng, Angle angleRng)
        {
            Cartridge = cart;
            EjectAngle = ejectAngle;
            VectRng = vectRng;
            AngleRng = angleRng;

        }

    }

    # endregion other stuff

}
