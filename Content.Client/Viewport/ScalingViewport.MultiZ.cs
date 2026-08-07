// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
// Ported to misfits-14
// #Cythisiax Ported — Multi-Z viewport rendering
//
// Approach matches CMU's RenderZLevelPasses: render levels bottom-to-top
// into the main viewport. First pass clears, subsequent passes don't,
// so empty tiles on upper levels reveal the level below.

using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private readonly IConfigurationManager _mzCfg = default!;
    [Dependency] private readonly IEntityManager _mzEntMan = default!;

    private bool _mzSkipNormalRender;

    private void MultiZBeforeRender()
    {
        _mzSkipNormalRender = false;

        if (_viewport == null || _eye == null)
            return;

        if (!_mzCfg.GetCVar(MZCVars.Enabled) || !_mzCfg.GetCVar(MZCVars.RenderEnabled))
            return;

        var playerManager = IoCManager.Resolve<IPlayerManager>();
        var player = playerManager.LocalSession?.AttachedEntity;
        if (player == null)
            return;

        if (!_mzEntMan.TryGetComponent<TransformComponent>(player, out var xform))
            return;

        if (xform.MapUid is not { } mapUid)
            return;

        if (!_mzEntMan.TryGetComponent<MZMapComponent>(mapUid, out var zMap))
            return;

        var zSystem = _mzEntMan.System<MZSharedSystem>();

        // Player is on an upper level with a map below - render the below map first
        if (!zSystem.TryMapDown((mapUid, zMap), out var belowMap))
            return;

        if (!_mzEntMan.TryGetComponent<MapComponent>(belowMap.Value, out var belowMC))
            return;

        // Save current eye, swap to below map, render with clear
        var savedEye = _viewport.Eye;
        var savedClear = _viewport.ClearColor;

        var belowCoords = new MapCoordinates(_eye.Position.Position, belowMC.MapId);
        _viewport.Eye = new Robust.Shared.Graphics.Eye
        {
            Position = belowCoords,
            Rotation = _eye.Rotation,
            Scale = _eye.Scale,
            DrawFov = _eye.DrawFov,
            DrawLight = _eye.DrawLight,
            Offset = _eye.Offset,
        };
        _viewport.ClearColor = Color.Black;
        _viewport.Render();

        // Restore original eye and do the current-map render on top.
        // ClearColor=null means no clear, so empty tiles reveal the below pass.
        _viewport.Eye = savedEye;
        _viewport.ClearColor = null;
        _viewport.Render();

        // Restore normal clear behavior for next frame
        _viewport.ClearColor = savedClear;
        _mzSkipNormalRender = true;
    }
}
