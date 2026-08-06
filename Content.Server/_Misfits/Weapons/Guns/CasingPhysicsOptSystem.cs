using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;

// #Misfits Add - sleep physics on spent casings on landing + enforce a global casing entity cap

namespace Content.Server._Misfits.Weapons.Guns;

/// <summary>
/// Server-side optimisation system for spent bullet casings:
/// <c>SharedGunSystem.OnCartEjected</c> relied on for optimizations
/// as it strips down physics but still allowing movement. Basically
/// remove collision physics on entity without turning CanCollide to false
/// nor without removing physComp or fixtureComp(causes gamestate sync issues)
///
/// <list type="number">
///   <item>sleeps <see cref="PhysicsComponent"/> on landing, stopping movement
///   by setting CanCollide off. </item>
///   <item>Enforces a global cap on concurrent casing entities. When the cap
///         is exceeded the oldest casings are deleted, preventing runaway
///         accumulation during sustained 20v20 firefights even with the
///         30-second <see cref="TimedDespawnComponent"/> timer.
///         </item>
/// </list>
///
/// <para>
/// The no-throw-angle edge case (revolver eject, manual cycling) is now handled
/// in <c>SharedGunSystem.OnCartEjected</c> which strips physics immediately
/// when <c>angle == null</c>.
/// </para>
/// </summary>
public sealed class CasingPhysicsOptSystem : EntitySystem
{
    /// <summary>
    /// Maximum number of spent casing entities allowed to exist at once.
    /// Beyond this, the oldest are deleted. 500 is generous — a 20-player
    /// war with automatic weapons peaks around 300-600 concurrent casings
    /// at 30 s lifetime.
    /// </summary>
    private const int MaxCasings = 500;

    // FIFO queue of tracked casing UIDs for cap enforcement.
    private readonly Queue<EntityUid> _casingQueue = new();
    [Dependency] private SharedPhysicsSystem _sharedPhysics = default!;
    public override void Initialize()
    {
        base.Initialize();

        // Sleep entities on landing
        SubscribeLocalEvent<CartridgeAmmoComponent, LandEvent>(OnCasingLand);

        // Track casings for the global cap when their despawn timer is attached.
        // This fires for ALL spent casings — both thrown and no-throw variants.
        SubscribeLocalEvent<CartridgeAmmoComponent, ComponentStartup>(OnCartridgeStartup);
    }

    /// <summary>
    /// Raised by <c>ThrownItemSystem</c> on ent's throw timer finishing
    /// Landed cart is set to rest
    /// </summary>
    private void OnCasingLand(EntityUid uid, CartridgeAmmoComponent cartridge, ref LandEvent args)
    {
        if (!cartridge.Spent || !TryComp<PhysicsComponent>(uid, out var physComp))
            return;

        // Misfit change: carts now removed from broadphase(reduced physics) on eject with CanCollide still on true
        //                to retain movement. This is when we explicitly tell the engine
        //                that the ent doesnt have physics by sleeping ent which sets CanCollide to false
        _sharedPhysics.SetAwake((uid, physComp), false);

    }

    /// <summary>
    /// Track spent casings for cap enforcement. We piggyback on ComponentStartup
    /// rather than adding a dedicated marker component.
    /// </summary>
    private void OnCartridgeStartup(EntityUid uid, CartridgeAmmoComponent cartridge, ComponentStartup args)
    {
        // Only track spent casings that have a despawn timer (i.e. ejected casings,
        // not cartridges sitting in a magazine).
        if (!cartridge.Spent || !HasComp<TimedDespawnComponent>(uid))
            return;

        _casingQueue.Enqueue(uid);
        TrimCasings();
    }

    /// <summary>
    /// Delete the oldest casings when the cap is exceeded.
    /// Skips already-deleted entities (natural despawn or manual cleanup).
    /// </summary>
    private void TrimCasings()
    {
        while (_casingQueue.Count > MaxCasings)
        {
            var oldest = _casingQueue.Dequeue();
            if (Exists(oldest) && !TerminatingOrDeleted(oldest))
                QueueDel(oldest);
        }
    }
}
