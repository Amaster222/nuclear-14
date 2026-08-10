// #Misfits Add - Flyable vertibird POC state and pilot action wiring.
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
