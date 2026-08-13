using Content.Shared._Misfits.Vehicles.Vertibird;
using Content.Shared.Input;
using Content.Shared.Movement.Systems;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client._Misfits.Vehicles.Vertibird;

public sealed class VertibirdPilotInputSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        var binds = CommandBinds.Builder;
        Bind(binds, EngineKeyFunctions.MoveUp, VertibirdControlInput.Forward);
        Bind(binds, EngineKeyFunctions.MoveDown, VertibirdControlInput.Back);
        Bind(binds, EngineKeyFunctions.MoveLeft, VertibirdControlInput.Left);
        Bind(binds, EngineKeyFunctions.MoveRight, VertibirdControlInput.Right);
        binds.Register<VertibirdPilotInputSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<VertibirdPilotInputSystem>();
    }

    private void Bind(CommandBinds.BindingsBuilder binds, BoundKeyFunction key, VertibirdControlInput input)
    {
        binds.BindBefore(key, new VertibirdMovementHandler(this, input), typeof(SharedMoverController));
    }

    private void SendInput(EntityUid? pilot, VertibirdControlInput input, bool pressed)
    {
        if (pilot is not { } uid || _player.LocalEntity != uid ||
            !IsCruisingPilot(uid))
            return;

        RaiseNetworkEvent(new VertibirdControlInputMessage(input, pressed));
    }

    private bool IsCruisingPilot(EntityUid pilot)
    {
        var query = EntityQueryEnumerator<VertibirdComponent>();
        while (query.MoveNext(out _, out var vertibird))
        {
            if (vertibird.Pilot == pilot && vertibird.State == VertibirdFlightState.Cruising)
                return true;
        }

        return false;
    }

    private sealed class VertibirdMovementHandler(VertibirdPilotInputSystem system, VertibirdControlInput input)
        : InputCmdHandler
    {
        private bool _passedDown;

        public override bool HandleCmdMessage(
            IEntityManager entManager,
            ICommonSession? session,
            IFullInputCmdMessage message)
        {
            if (message.State == BoundKeyState.Down)
            {
                var block = session?.AttachedEntity is { } pilot && system.IsCruisingPilot(pilot);
                _passedDown = !block;

                if (block)
                    system.SendInput(session!.AttachedEntity, input, true);

                return block;
            }

            if (message.State == BoundKeyState.Up && _passedDown)
            {
                _passedDown = false;
                return false;
            }

            if (session?.AttachedEntity is not { } releasePilot || !system.IsCruisingPilot(releasePilot))
                return false;

            system.SendInput(releasePilot, input, false);
            return true;
        }
    }
}
