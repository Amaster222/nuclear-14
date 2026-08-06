using Content.Shared._Misfits.Engraving;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Ranged.Systems;
// TODO Misfit: refactor spent cartridges to be a visual effect. gotta see how game does dynamic animation or have client only spawned spent cart proto

public abstract partial class SharedGunSystem
{
    [Dependency] protected private IGameTiming _timing = default!;

    /// Event we are subbed to is raised in different contexts by server and client
    /// LocalSub is in shared code that server and client run everytime with little variation
    /// since it is predictively raised on the clientside so the event is sent to server

    /// networksub is needed tho since guncode triggered serverside(NPCs, autofire)
    /// doesnt network the same events to the client
    /// normally isnt an issue as client/server state also resolved by networked comps
    /// but comps stripped from carts get rebuilt when networked, so we need
    /// the server to explcitly network the event to restrip the carts

    /// this
    protected virtual void InitializeCartridge()
    {
        SubscribeNetworkEvent<EjectSpentCartEvent>(OnCartEjected); // only server sends to clients so they do same code

        /// already ran predictvely from client GunSystem <see cref="RequestShootEvent"/> which calls shared code
        /// so ran by both server and client
        SubscribeLocalEvent<CartridgeAmmoComponent, EjectSpentCartEvent>(OnCartEjected);
    }


    /// <summary>
    /// Main method meant to strip down spent cartridges so they are a non-interactable visual
    /// with a short despawn and minimal preformance impact from presence.
    /// Main benefit is that cartrides will have physics(look animated) but wont clutter up the broadphase
    /// (broadphase is a data struct for fast physics look ups for things like collisions)
    ///
    /// Works for server and client(kinda). Spent Cartridges generated from client's entering/REntering its PVS range
    /// networks it without events so fixture comp is regen'd
    /// but not other comps that'll make carts interactable or cause issues(luckily)
    ///
    /// <summary/>
    /// <remarks>
    /// This is pretty jank to do in my view, since we might as well just make cartridges a visual effect
    /// here we waste time checking for cartridges and then stripping them down to be a visual
    /// this is done at least twice, since any cartridges networked need to be stripped down again
    ///
    /// Specifically the fixture comp gets rebuilt and removing the comp itself causes desync issues
    /// TLDR of why that happens is anything with a physics comp
    /// is mostly assumed to have a fixture
    /// <remarks/>
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
            TransformSystem.SetLocalPositionRotation(uid, Transform(uid).Coordinates.Position + args.VectRng,
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


        TransformSystem.SetLocalRotation(uid, args.AngleRng);
        Angle ejectAngle = args.EjectAngle.Value;
        ejectAngle += 3.7f; // 212 degrees; casings should eject slightly to the right and behind of a gun
        ThrowingSystem.TryThrow(uid, ejectAngle.ToVec() + args.VectRng, 5f);
    }

    /// <summary>
    /// Networked wrapper for above so clients can run same logic even if originally from server
    /// validates netID and comp
    /// </summary>
    public void OnCartEjected(EjectSpentCartEvent args)
    {
        Log.Debug($"Spent Cartridge from server NetID: {args.Cartridge}");
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
