// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
// Ported to misfits-14
// #Cythisiax Ported — Multi-Z viewport rendering
//
// Renders the level below into an offscreen viewport and composites it
// at FaintUpperAlpha. Matches CMU's RenderFaintUpperComposite approach.
// DrawFov=true so entities render; minor FOV cone / space edge artifacts
// at low alpha are acceptable. Stencil masking for per-tile occlusion
// can be added later.

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

    private IClydeViewport? _mzBelowViewport;
    private bool _mzDrawBelow;
    private float _mzBelowAlpha;

    private void MultiZBeforeRender()
    {
        _mzDrawBelow = false;

        if (!_mzCfg.GetCVar(MZCVars.Enabled) || !_mzCfg.GetCVar(MZCVars.RenderEnabled))
            return;

        if (_eye is not { } eye || _viewport == null)
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

        if (!zSystem.TryMapDown((mapUid, zMap), out var belowMap))
            return;

        if (!_mzEntMan.TryGetComponent<MapComponent>(belowMap.Value, out var belowMC))
            return;

        EnsureBelowViewport();
        if (_mzBelowViewport == null)
            return;

        _mzBelowViewport.RenderScale = _viewport.RenderScale;

        var belowCoords = new MapCoordinates(eye.Position.Position, belowMC.MapId);
        _mzBelowViewport.Eye = new Robust.Shared.Graphics.Eye
        {
            Position = belowCoords,
            Rotation = eye.Rotation,
            Scale = eye.Scale,
            DrawFov = eye.DrawFov,
            DrawLight = eye.DrawLight,
            Offset = eye.Offset,
        };
        _mzBelowViewport.ClearColor = Color.Transparent;
        _mzBelowViewport.Render();

        _mzBelowAlpha = _mzCfg.GetCVar(MZCVars.FaintUpperAlpha);
        _mzDrawBelow = true;
    }

    private void MultiZDrawComposite(IRenderHandle handle, UIBox2i drawBox)
    {
        if (!_mzDrawBelow || _mzBelowViewport == null)
            return;

        handle.DrawingHandleScreen.DrawTextureRect(
            _mzBelowViewport.RenderTarget.Texture,
            drawBox,
            Color.White.WithAlpha(_mzBelowAlpha));
    }

    private void EnsureBelowViewport()
    {
        if (_viewport == null)
            return;

        if (_mzBelowViewport != null &&
            _mzBelowViewport.Size == _viewport.Size)
            return;

        _mzBelowViewport?.Dispose();
        _mzBelowViewport = _clyde.CreateViewport(
            _viewport.Size,
            TextureSampleParameters.Default,
            "multi-z-below");
    }
}
