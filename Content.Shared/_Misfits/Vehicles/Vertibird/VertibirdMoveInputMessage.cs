// #Misfits Add - Client→server WASD input relay for vertibird pilots.
// The pilot's InputMoverComponent.HeldMoveButtons are sent to the server
// every time they change, bypassing the relay/HandleMobMovement system
// which conflicts with the vertibird's custom drift physics.

using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Vehicles.Vertibird;

[Serializable, NetSerializable]
public sealed class VertibirdMoveInputMessage : EntityEventArgs
{
    public NetEntity Vertibird;
    public MoveButtons Buttons;

    public VertibirdMoveInputMessage(NetEntity vertibird, MoveButtons buttons)
    {
        Vertibird = vertibird;
        Buttons = buttons;
    }
}
