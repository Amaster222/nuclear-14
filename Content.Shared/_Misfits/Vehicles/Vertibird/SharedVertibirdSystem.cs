using Content.Shared.Buckle.Components;
using Content.Shared.Projectiles;

namespace Content.Shared._Misfits.Vehicles.Vertibird;

/// <summary>
/// Shared collision rules for weapons fired from inside a Vertibird. Running
/// this on both client and server keeps predicted projectiles from striking the
/// aircraft or another occupant sharing its cabin origin.
/// </summary>
public sealed class SharedVertibirdSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        // #Misfits Edited - Subscribe to the broadcast ProjectilePreventCollideEvent instead of
        // PreventCollideEvent directly (the event bus allows only one directed subscription per
        // component/event pair, which the base SharedProjectileSystem already owns).
        SubscribeLocalEvent<ProjectilePreventCollideEvent>(OnProjectilePreventCollide);
    }

    private void OnProjectilePreventCollide(ref ProjectilePreventCollideEvent args)
    {
        var projectile = args.Projectile;

        // The aircraft has no projectile health pool: rounds pass through its
        // broad fixture and strike the occupants sharing its cabin origin.
        if (HasComp<VertibirdComponent>(args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }

        // A passenger firing outward should not immediately hit another
        // passenger stacked at the same hidden cabin origin.
        if (projectile.Comp.Shooter is not { } shooter ||
            !TryComp<BuckleComponent>(shooter, out var shooterBuckle) ||
            shooterBuckle.BuckledTo is not { } vertibird ||
            !HasComp<VertibirdComponent>(vertibird))
        {
            return;
        }

        if (TryComp<BuckleComponent>(args.OtherEntity, out var otherBuckle) &&
            otherBuckle.BuckledTo == vertibird)
        {
            args.Cancelled = true;
        }
    }
}
