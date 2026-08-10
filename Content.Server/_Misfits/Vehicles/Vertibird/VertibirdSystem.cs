// #Misfits Add - Server-side flyable vertibird POC.
using Content.Shared._Misfits.Vehicles.Vertibird;
using Content.Shared._MultiZ.Core.Components;
using Content.Server._MultiZ.Core;
using Content.Shared.Actions;
using Content.Shared.Buckle.Components;
using Content.Shared.Popups;

namespace Content.Server._Misfits.Vehicles.Vertibird;

public sealed partial class VertibirdSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private MZSystem _multiZ = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VertibirdComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<VertibirdComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<VertibirdComponent, UnstrapAttemptEvent>(OnUnstrapAttempt);
        SubscribeLocalEvent<VertibirdComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<VertibirdComponent, VertibirdFlightActionEvent>(OnFlightAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<VertibirdComponent, MZPhysicsComponent>();
        while (query.MoveNext(out var uid, out var vertibird, out var mzPhysics))
        {
            switch (vertibird.State)
            {
                case VertibirdFlightState.TakingOff:
                    UpdateTakeoff(uid, vertibird, mzPhysics, frameTime);
                    break;
                case VertibirdFlightState.Landing:
                    UpdateLanding(uid, vertibird, mzPhysics, frameTime);
                    break;
                case VertibirdFlightState.Cruising:
                    HoldHover(uid, vertibird, mzPhysics);
                    break;
            }
        }
    }

    private void OnStrapAttempt(Entity<VertibirdComponent> ent, ref StrapAttemptEvent args)
    {
        var pilot = args.Buckle.Owner;
        if (HasComp<VertibirdPilotPerkComponent>(pilot))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("vertibird-pilot-required"), ent, pilot);
    }

    private void OnStrapped(Entity<VertibirdComponent> ent, ref StrappedEvent args)
    {
        var pilot = args.Buckle.Owner;
        if (!HasComp<VertibirdPilotPerkComponent>(pilot))
            return;

        ent.Comp.Pilot = pilot;
        _actions.AddAction(pilot, ref ent.Comp.FlightActionEntity, ent.Comp.FlightAction, ent.Owner);
        Dirty(ent);
    }

    private void OnUnstrapAttempt(Entity<VertibirdComponent> ent, ref UnstrapAttemptEvent args)
    {
        if (ent.Comp.State == VertibirdFlightState.Grounded)
            return;

        if (args.User == null || args.User != args.Buckle.Owner)
            return;

        args.Cancelled = true;

        if (args.Popup)
            _popup.PopupEntity(Loc.GetString("vertibird-unbuckle-blocked"), ent, args.User.Value);
    }

    private void OnUnstrapped(Entity<VertibirdComponent> ent, ref UnstrappedEvent args)
    {
        if (ent.Comp.Pilot != args.Buckle.Owner)
            return;

        RemovePilotAction(args.Buckle.Owner, ent.Comp);
        ent.Comp.Pilot = null;
        Dirty(ent);
    }

    private void OnFlightAction(Entity<VertibirdComponent> ent, ref VertibirdFlightActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Pilot != args.Performer)
            return;

        if (!HasComp<VertibirdPilotPerkComponent>(args.Performer))
        {
            _popup.PopupEntity(Loc.GetString("vertibird-pilot-required"), ent, args.Performer);
            return;
        }

        switch (ent.Comp.State)
        {
            case VertibirdFlightState.Grounded:
                StartTakeoff(ent);
                args.Handled = true;
                break;
            case VertibirdFlightState.TakingOff:
            case VertibirdFlightState.Cruising:
                StartLanding(ent);
                args.Handled = true;
                break;
        }
    }

    private void StartTakeoff(Entity<VertibirdComponent> ent)
    {
        if (!TryComp<MZPhysicsComponent>(ent, out var mzPhysics))
            return;

        var coords = _transform.GetMapCoordinates(ent.Owner);
        _transform.SetMapCoordinates(ent.Owner, coords);

        mzPhysics.Velocity = 0f;
        ent.Comp.State = VertibirdFlightState.TakingOff;
        Dirty(ent);
        _multiZ.WakeZPhysics((ent.Owner, mzPhysics));
    }

    private void StartLanding(Entity<VertibirdComponent> ent)
    {
        if (!TryComp<MZPhysicsComponent>(ent, out var mzPhysics))
            return;

        mzPhysics.Velocity = 0f;
        ent.Comp.State = VertibirdFlightState.Landing;
        Dirty(ent);
        _multiZ.WakeZPhysics((ent.Owner, mzPhysics));
    }

    private void UpdateTakeoff(EntityUid uid, VertibirdComponent vertibird, MZPhysicsComponent mzPhysics, float frameTime)
    {
        var next = MathF.Min(vertibird.HoverAltitude, mzPhysics.LocalPosition + vertibird.VerticalSpeed * frameTime);
        _multiZ.SetZLocalPosition((uid, mzPhysics), next);
        mzPhysics.Velocity = 0f;

        if (next < vertibird.HoverAltitude)
            return;

        vertibird.State = VertibirdFlightState.Cruising;
        Dirty(uid, vertibird);
    }

    private void UpdateLanding(EntityUid uid, VertibirdComponent vertibird, MZPhysicsComponent mzPhysics, float frameTime)
    {
        var next = MathF.Max(0f, mzPhysics.LocalPosition - vertibird.VerticalSpeed * frameTime);
        _multiZ.SetZLocalPosition((uid, mzPhysics), next);
        mzPhysics.Velocity = 0f;

        if (next > 0f)
            return;

        vertibird.State = VertibirdFlightState.Grounded;
        Dirty(uid, vertibird);
        RemComp<MZFallingComponent>(uid);
    }

    private void HoldHover(EntityUid uid, VertibirdComponent vertibird, MZPhysicsComponent mzPhysics)
    {
        _multiZ.SetZLocalPosition((uid, mzPhysics), vertibird.HoverAltitude);
        mzPhysics.Velocity = 0f;
    }

    private void RemovePilotAction(EntityUid pilot, VertibirdComponent vertibird)
    {
        if (vertibird.FlightActionEntity == null)
            return;

        _actions.RemoveAction(pilot, vertibird.FlightActionEntity);
        vertibird.FlightActionEntity = null;
    }
}
