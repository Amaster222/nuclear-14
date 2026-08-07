// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
// Ported to misfits-14 — simplified initial version
// #Cythisiax Ported — Multi-Z viewport rendering

using System.Numerics;
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

    private IClydeViewport? _mzOffscreenViewport;
    private bool _mzDrawComposite;
    private float _mzCompositeAlpha;

    private void MultiZBeforeRender()
    {
        _mzDrawComposite = false;

        if (!_mzCfg.GetCVar(MZCVars.Enabled) || !_mzCfg.GetCVar(MZCVars.RenderEnabled))
            return;

        if (_eye is not { } eye)
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

        // Try to render the map below (looking down from an upper level)
        if (zSystem.TryMapDown((mapUid, zMap), out var belowMap))
        {
            if (!_mzEntMan.TryGetComponent<MapComponent>(belowMap.Value, out var belowMC))
                return;

            EnsureMultiZViewport();
            if (_mzOffscreenViewport == null || _viewport == null)
                return;

            _mzOffscreenViewport.RenderScale = _viewport.RenderScale;
            var mapCoords = new MapCoordinates(eye.Position.Position, belowMC.MapId);
            var zEye = new Robust.Shared.Graphics.Eye
            {
                Position = mapCoords,
                Rotation = eye.Rotation,
                Scale = eye.Scale,
                DrawFov = true,
                DrawLight = true,
                Offset = eye.Offset,
            };
            _mzOffscreenViewport.Eye = zEye;

            _mzOffscreenViewport.ClearColor = Color.Transparent;
            _mzOffscreenViewport.Render();
            _mzCompositeAlpha = _mzCfg.GetCVar(MZCVars.FaintUpperAlpha);
            _mzDrawComposite = true;
        }
    }

    private void MultiZDrawComposite(IRenderHandle handle, UIBox2i drawBox)
    {
        if (!_mzDrawComposite || _mzOffscreenViewport == null)
            return;

        handle.DrawingHandleScreen.DrawTextureRect(
            _mzOffscreenViewport.RenderTarget.Texture,
            drawBox,
            Color.White.WithAlpha(_mzCompositeAlpha));
    }

    private void EnsureMultiZViewport()
    {
        if (_viewport == null)
            return;

        if (_mzOffscreenViewport != null &&
            _mzOffscreenViewport.Size == _viewport.Size)
            return;

        _mzOffscreenViewport?.Dispose();
        _mzOffscreenViewport = _clyde.CreateViewport(
            _viewport.Size,
            TextureSampleParameters.Default,
            "multi-z-offscreen");
    }
}
