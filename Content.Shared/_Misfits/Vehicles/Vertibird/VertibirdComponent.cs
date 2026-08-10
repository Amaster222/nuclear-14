// #Misfits Add - Flyable vertibird POC state and pilot action wiring.
using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Vehicles.Vertibird;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VertibirdComponent : Component
{
    [DataField, AutoNetworkedField]
    public VertibirdFlightState State = VertibirdFlightState.Grounded;

    [DataField, AutoNetworkedField]
    public EntityUid? Pilot;

    [DataField, AutoNetworkedField]
    public EntityUid? FlightActionEntity;

    [DataField]
    public EntProtoId FlightAction = "ActionVertibirdTakeOff";

    [DataField]
    public float HoverAltitude = 0.35f;

    [DataField]
    public float VerticalSpeed = 0.25f;

    [DataField]
    public float ThrustAcceleration = 6f;

    [DataField]
    public float ReverseAcceleration = 2f;

    [DataField]
    public float MaxFlightSpeed = 12f;

    [DataField]
    public float FlightDrag = 0.75f;

    [DataField]
    public float TurnSpeedDegrees = 90f;

    [DataField, AutoNetworkedField]
    public Vector2 DriftVelocity = Vector2.Zero;

    [DataField]
    public string MapConfigId = "Wendover";
}

public enum VertibirdFlightState : byte
{
    Grounded,
    TakingOff,
    Cruising,
    Landing,
}

public sealed partial class VertibirdFlightActionEvent : InstantActionEvent;
