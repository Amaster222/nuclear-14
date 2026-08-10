// #Misfits Add - Server-side flyable vertibird POC.
using System.Numerics;
using Content.Shared._Misfits.Vehicles.Vertibird;
using Content.Shared._MultiZ.Core.Components;
using Content.Server._MultiZ.Core;
using Content.Shared.Actions;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;

namespace Content.Server._Misfits.Vehicles.Vertibird;

public sealed partial class VertibirdSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private MZSystem _multiZ = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMoverController _mover = default!;
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
        SubscribeLocalEvent<VertibirdComponent, VertibirdLandActionEvent>(OnLandAction);
        SubscribeLocalEvent<VertibirdComponent, VertibirdMoveUpActionEvent>(OnMoveUpAction);
        SubscribeLocalEvent<VertibirdComponent, VertibirdMoveDownActionEvent>(OnMoveDownAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<VertibirdComponent, MZPhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vertibird, out var mzPhysics, out var xform))
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
                    UpdateCruising(uid, vertibird, xform, frameTime);
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
        EnsureComp<InputMoverComponent>(ent.Owner);
        _mover.SetRelay(pilot, ent.Owner);
        _interaction.SetRelay(pilot, ent.Owner, EnsureComp<InteractionRelayComponent>(pilot));
        AddPilotActions(pilot, ent);
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
        RemovePilotRelay(args.Buckle.Owner, ent.Owner);
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
        }
    }

    private void OnLandAction(Entity<VertibirdComponent> ent, ref VertibirdLandActionEvent args)
    {
        if (args.Handled || !CanUsePilotAction(ent, args.Performer))
            return;

        if (ent.Comp.State is VertibirdFlightState.TakingOff or VertibirdFlightState.Cruising)
        {
            StartLanding(ent);
            args.Handled = true;
        }
    }

    private void OnMoveUpAction(Entity<VertibirdComponent> ent, ref VertibirdMoveUpActionEvent args)
    {
        if (args.Handled || !CanUsePilotAction(ent, args.Performer))
            return;

        args.Handled = TryMoveZ(ent, 1);
    }

    private void OnMoveDownAction(Entity<VertibirdComponent> ent, ref VertibirdMoveDownActionEvent args)
    {
        if (args.Handled || !CanUsePilotAction(ent, args.Performer))
            return;

        args.Handled = TryMoveZ(ent, -1);
    }

    private bool CanUsePilotAction(Entity<VertibirdComponent> ent, EntityUid performer)
    {
        if (ent.Comp.Pilot != performer)
            return false;

        if (HasComp<VertibirdPilotPerkComponent>(performer))
            return true;

        _popup.PopupEntity(Loc.GetString("vertibird-pilot-required"), ent, performer);
        return false;
    }

    private void StartTakeoff(Entity<VertibirdComponent> ent)
    {
        if (!TryComp<MZPhysicsComponent>(ent, out var mzPhysics))
            return;

        var coords = _transform.GetMapCoordinates(ent.Owner);
        _transform.SetMapCoordinates(ent.Owner, coords);

        mzPhysics.Velocity = 0f;
        ent.Comp.State = VertibirdFlightState.TakingOff;
        ent.Comp.DriftVelocity = Vector2.Zero;
        Dirty(ent);
        _multiZ.WakeZPhysics((ent.Owner, mzPhysics));
    }

    private void StartLanding(Entity<VertibirdComponent> ent)
    {
        if (!TryComp<MZPhysicsComponent>(ent, out var mzPhysics))
            return;

        mzPhysics.Velocity = 0f;
        ent.Comp.State = VertibirdFlightState.Landing;
        ent.Comp.DriftVelocity = Vector2.Zero;
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
        vertibird.DriftVelocity = Vector2.Zero;

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

    private void UpdateCruising(EntityUid uid, VertibirdComponent vertibird, TransformComponent xform, float frameTime)
    {
        if (!TryComp<InputMoverComponent>(uid, out var input))
            return;

        var buttons = SharedMoverController.GetNormalizedMovement(input.HeldMoveButtons);
        var rotation = _transform.GetWorldRotation(xform);
        var turn = 0f;

        if ((buttons & MoveButtons.Left) != 0)
            turn += 1f;

        if ((buttons & MoveButtons.Right) != 0)
            turn -= 1f;

        if (turn != 0f)
            rotation += Angle.FromDegrees(vertibird.TurnSpeedDegrees * turn * frameTime);

        var thrust = Vector2.Zero;
        var forward = rotation.ToWorldVec();

        if ((buttons & MoveButtons.Up) != 0)
            thrust += forward * vertibird.ThrustAcceleration;

        if ((buttons & MoveButtons.Down) != 0)
            thrust -= forward * vertibird.ReverseAcceleration;

        vertibird.DriftVelocity += thrust * frameTime;

        if (thrust == Vector2.Zero)
        {
            var drag = MathF.Max(0f, 1f - vertibird.FlightDrag * frameTime);
            vertibird.DriftVelocity *= drag;
        }

        var speed = vertibird.DriftVelocity.Length();
        if (speed > vertibird.MaxFlightSpeed)
            vertibird.DriftVelocity = vertibird.DriftVelocity / speed * vertibird.MaxFlightSpeed;

        var next = _transform.GetWorldPosition(xform) + vertibird.DriftVelocity * frameTime;
        _transform.SetWorldPositionRotation(uid, next, rotation, xform);
        Dirty(uid, vertibird);
    }

    private bool TryMoveZ(Entity<VertibirdComponent> ent, int offset)
    {
        if (ent.Comp.State != VertibirdFlightState.Cruising)
            return false;

        if (!TryComp<MZPhysicsComponent>(ent, out var mzPhysics))
            return false;

        var moved = offset > 0
            ? _multiZ.TryMoveUp(ent.Owner)
            : _multiZ.TryMoveDown(ent.Owner);

        if (!moved)
            return false;

        mzPhysics.LocalPosition = ent.Comp.HoverAltitude;
        mzPhysics.Velocity = 0f;
        ent.Comp.DriftVelocity = Vector2.Zero;
        Dirty(ent);
        _multiZ.WakeZPhysics((ent.Owner, mzPhysics));
        return true;
    }

    private void AddPilotActions(EntityUid pilot, Entity<VertibirdComponent> ent)
    {
        _actions.AddAction(pilot, ref ent.Comp.FlightActionEntity, ent.Comp.FlightAction, ent.Owner);
        _actions.AddAction(pilot, ref ent.Comp.LandActionEntity, ent.Comp.LandAction, ent.Owner);
        _actions.AddAction(pilot, ref ent.Comp.MoveUpActionEntity, ent.Comp.MoveUpAction, ent.Owner);
        _actions.AddAction(pilot, ref ent.Comp.MoveDownActionEntity, ent.Comp.MoveDownAction, ent.Owner);
    }

    private void RemovePilotAction(EntityUid pilot, VertibirdComponent vertibird)
    {
        _actions.RemoveAction(pilot, vertibird.FlightActionEntity);
        _actions.RemoveAction(pilot, vertibird.LandActionEntity);
        _actions.RemoveAction(pilot, vertibird.MoveUpActionEntity);
        _actions.RemoveAction(pilot, vertibird.MoveDownActionEntity);
        vertibird.FlightActionEntity = null;
        vertibird.LandActionEntity = null;
        vertibird.MoveUpActionEntity = null;
        vertibird.MoveDownActionEntity = null;
    }

    private void RemovePilotRelay(EntityUid pilot, EntityUid vertibird)
    {
        if (TryComp<RelayInputMoverComponent>(pilot, out var relay) && relay.RelayEntity == vertibird)
            RemComp<RelayInputMoverComponent>(pilot);

        if (TryComp<InteractionRelayComponent>(pilot, out var interactionRelay) && interactionRelay.RelayEntity == vertibird)
            RemComp<InteractionRelayComponent>(pilot);
    }
}
