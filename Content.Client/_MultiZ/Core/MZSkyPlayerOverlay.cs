// Origin: misfits-14 _MultiZ
// #Cythisiax Add - visible player marker while viewing from empty sky layers

using System.Numerics;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._MultiZ.Core;

/// <summary>
/// Draws a lightweight marker for the local player while the empty sky layer is
/// rendered as the lower map. The player entity itself lives on the sky map, so
/// it is not part of the lower-map viewport pass.
/// </summary>
public sealed class MZSkyPlayerOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public MZSkyPlayerOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_cfg.GetCVar(MZCVars.Enabled) || !_cfg.GetCVar(MZCVars.RenderEnabled))
            return;

        var player = _player.LocalSession?.AttachedEntity;
        if (player == null)
            return;

        if (!_entMan.TryGetComponent<TransformComponent>(player.Value, out var xform) ||
            xform.MapUid is not { } mapUid ||
            !_entMan.TryGetComponent<MZMapComponent>(mapUid, out _))
        {
            return;
        }

        if (HasRenderableGrids(mapUid))
            return;

        if (args.ViewportControl == null ||
            !_entMan.TryGetComponent<SpriteComponent>(player.Value, out var sprite))
        {
            return;
        }

        var worldPos = _entMan.System<SharedTransformSystem>().GetWorldPosition(xform);
        var screenPos = args.ViewportControl.WorldToScreen(worldPos);
        var scale = args.Viewport.Eye?.Zoom ?? Vector2.One;

        args.ScreenHandle.DrawEntity(
            player.Value,
            screenPos,
            scale,
            null,
            args.Viewport.Eye?.Rotation ?? default,
            sprite: sprite,
            xform: xform,
            xformSystem: _entMan.System<SharedTransformSystem>());
    }

    private bool HasRenderableGrids(EntityUid mapUid)
    {
        var query = _entMan.EntityQueryEnumerator<TransformComponent, MapGridComponent>();
        while (query.MoveNext(out _, out var xform, out _))
        {
            if (xform.MapUid == mapUid)
                return true;
        }

        return false;
    }
}
