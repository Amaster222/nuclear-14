// #Misfits Add - Client-side WASD forwarder for vertibird pilots.
// Subscribes to MoveInputEvent on the local player. When the local player
// is buckled as a vertibird pilot, sends the raw HeldMoveButtons to the
// server via VertibirdMoveInputMessage. This completely bypasses the
// InputMoverComponent/relay/HandleMobMovement pipeline.

using Content.Shared._Misfits.Vehicles.Vertibird;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Client.Player;

namespace Content.Client._Misfits.Vehicles.Vertibird;

public sealed class VertibirdMoveInputSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InputMoverComponent, MoveInputEvent>(OnMoveInput);
    }

    private void OnMoveInput(Entity<InputMoverComponent> ent, ref MoveInputEvent args)
    {
        // Only forward input from the local player.
        var local = _player.LocalEntity;
        if (local == null || ent.Owner != local.Value)
            return;

        // Find the vertibird this player is piloting.
        var query = EntityQueryEnumerator<VertibirdComponent>();
        while (query.MoveNext(out var vbUid, out var vertibird))
        {
            if (vertibird.Pilot == ent.Owner)
            {
                RaiseNetworkEvent(new VertibirdMoveInputMessage(GetNetEntity(vbUid), args.Entity.Comp.HeldMoveButtons));
                return;
            }
        }
    }
}
