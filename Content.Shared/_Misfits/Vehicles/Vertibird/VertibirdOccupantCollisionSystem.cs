// #Misfits Fix - Stop a seated occupant from physically colliding with the aircraft they
// are sitting inside.
//
// The vertibird straps occupants at buckleOffset 0,0, which parks them dead centre inside
// the craft's own 3.2 by 1.2 hull fixture. That is a deep overlap, so the solver pushes hard
// to separate the two bodies. Normally the occupant absorbs most of that and nothing visible
// happens. A power armour wearer refuses it: PowerArmorWornComponent cancels
// AttemptMobTargetCollideEvent so the wearer acts as an immovable wall. The entire separation
// impulse then lands on the craft, which is a dynamic body, and it gets thrown across the pad.
//
// The engine already supports suppressing this, but only through BuckleComponent.DontCollide,
// which is a datafield on the person rather than something a strap can ask for, so a vehicle
// cannot opt its passengers in. The craft refuses the contact itself instead.
using Content.Shared.Buckle.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Misfits.Vehicles.Vertibird;

public sealed class VertibirdOccupantCollisionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VertibirdComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<VertibirdComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        // Only occupants of this specific craft pass through. Everyone else still finds the
        // hull solid, so the vertibird keeps blocking movement and taking impacts as before.
        if (TryComp<BuckleComponent>(args.OtherEntity, out var buckle) && buckle.BuckledTo == ent.Owner)
            args.Cancelled = true;
    }
}
