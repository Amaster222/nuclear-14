// #Misfits Add - Flyable vertibird POC state and pilot action wiring.
using System.Numerics;
using System;
using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Vehicles.Vertibird;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VertibirdComponent : Component
{
    [DataField, AutoNetworkedField]
    public VertibirdFlightState State = VertibirdFlightState.Grounded;

    [DataField, AutoNetworkedField]
    public EntityUid? Pilot;

    [ViewVariables]
    public EntityUid?[] SeatOccupants = new EntityUid?[9];

    [DataField, AutoNetworkedField]
    public EntityUid? FlightActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? LandActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? MoveUpActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? MoveDownActionEntity;

    [DataField]
    public EntProtoId FlightAction = "ActionVertibirdTakeOff";

    [DataField]
    public EntProtoId LandAction = "ActionVertibirdLand";

    [DataField]
    public EntProtoId MoveUpAction = "ActionVertibirdMoveUp";

    [DataField]
    public EntProtoId MoveDownAction = "ActionVertibirdMoveDown";

    [DataField]
    public float StartupDuration = 35f;

    public TimeSpan StartupStartedAt = TimeSpan.Zero;

    public TimeSpan StartupFinishedAt = TimeSpan.Zero;

    public int StartupEmoteIndex;

    [DataField]
    public SoundSpecifier? StartupSound = new SoundPathSpecifier("/Audio/_Misfits/N14/Vehicles/vertibird_start.ogg",
        AudioParams.Default.WithVolume(-1f).WithMaxDistance(18f));

    [DataField]
    public SoundSpecifier? FlightLoopSound = new SoundPathSpecifier("/Audio/_Misfits/N14/Vehicles/vertibird_loop.ogg",
        AudioParams.Default.WithLoop(true).WithVolume(-2f).WithMaxDistance(18f));

    [DataField]
    public SoundSpecifier? LandingSound = new SoundPathSpecifier("/Audio/_Misfits/N14/Vehicles/vertibird_stop.ogg",
        AudioParams.Default.WithVolume(-1f).WithMaxDistance(18f));

    public EntityUid? StartupSoundStream;

    public EntityUid? FlightSoundStream;

    [DataField]
    public float HoverAltitude = 0.85f;

    [DataField]
    public float VerticalSpeed = 0.75f;

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

    [ViewVariables]
    public VertibirdControlInput HeldInputs;

    [ViewVariables]
    public TimeSpan AltitudeTransitionFinishedAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid? AltitudeTargetMap;

    [ViewVariables]
    public int AltitudeOffset;

    [DataField]
    public string MapConfigId = "Wendover";
}

[Flags]
public enum VertibirdControlInput : byte
{
    None = 0,
    Forward = 1 << 0,
    Back = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
}

[RegisterComponent]
public sealed partial class VertibirdHiddenOccupantComponent : Component
{
    [DataField]
    public bool HadStealth;

    [DataField]
    public float PreviousVisibility = 1f;
}

[Serializable, NetSerializable]
public enum VertibirdUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class VertibirdSeatBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly VertibirdFlightState FlightState;
    public readonly VertibirdSeatUiState[] Seats;

    public VertibirdSeatBoundUserInterfaceState(VertibirdFlightState flightState, VertibirdSeatUiState[] seats)
    {
        FlightState = flightState;
        Seats = seats;
    }
}

[Serializable, NetSerializable]
public readonly record struct VertibirdSeatUiState(int Index, string Name, string? OccupantName, bool RequiresPilotPerk);

[Serializable, NetSerializable]
public sealed class VertibirdSelectSeatMessage : BoundUserInterfaceMessage
{
    public readonly int SeatIndex;

    public VertibirdSelectSeatMessage(int seatIndex)
    {
        SeatIndex = seatIndex;
    }
}

[Serializable, NetSerializable]
public sealed class VertibirdControlInputMessage : EntityEventArgs
{
    public VertibirdControlInput Input;
    public bool Pressed;

    public VertibirdControlInputMessage(VertibirdControlInput input, bool pressed)
    {
        Input = input;
        Pressed = pressed;
    }
}

public enum VertibirdFlightState : byte
{
    Grounded,
    Starting,
    TakingOff,
    Cruising,
    ChangingAltitude,
    Landing,
}

public sealed partial class VertibirdFlightActionEvent : InstantActionEvent;

public sealed partial class VertibirdLandActionEvent : InstantActionEvent;

public sealed partial class VertibirdMoveUpActionEvent : InstantActionEvent;

public sealed partial class VertibirdMoveDownActionEvent : InstantActionEvent;
