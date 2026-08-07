// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
//   PR #1058 "Multi Z addition" & #1119 "Multi z fixes" by TheHellFireo
//   Based on Crystall Edge (crystallpunk-14) Multi-Z system
// Ported to misfits-14 _MultiZ/ — renamed &amp; adapted
// #Cythisiax Ported — Multi-Z level support for misfits-14

using System.Numerics;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core;
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
/// Renders entities from adjacent Z-levels through openings at a visual offset.
/// Entities on the level above are rendered shifted upward.
/// </summary>
public sealed class MZVisibleEntityOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IEyeManager _eye = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private readonly MZOpeningCache _openingCache = new();
    private readonly List<Entity<MapGridComponent>> _gridScratch = new();

    public MZVisibleEntityOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_cfg.GetCVar(MZCVars.VisibleEntityIndicators) || !_cfg.GetCVar(MZCVars.RenderEnabled))
            return;

        var player = _player.LocalSession?.AttachedEntity;
        if (player == null || !_entMan.TryGetComponent<MZViewerComponent>(player.Value, out var viewer))
            return;

        if (!viewer.LookUp && !viewer.FaintUp && !viewer.StairPreviewUp)
            return;

        var spriteSystem = _entMan.System<SpriteSystem>();
        var transformSystem = _entMan.System<SharedTransformSystem>();
        var mapSystem = _entMan.System<SharedMapSystem>();

        var playerXform = _entMan.GetComponent<TransformComponent>(player.Value);
        if (playerXform.MapUid is not { } mapUid)
            return;

        if (!_entMan.TryGetComponent<MZMapComponent>(mapUid, out var zMap))
            return;

        // Try to get the map above
        var zSystem = _entMan.System<MZSharedSystem>();
        if (!zSystem.TryMapUp((mapUid, zMap), out var aboveMap))
            return;

        if (!_entMan.TryGetComponent<MapGridComponent>(aboveMap.Value, out var aboveGrid))
            return;

        var alpha = viewer.LookUp ? 1f : _cfg.GetCVar(MZCVars.FaintUpperAlpha);
        var offset = new Vector2(0, MZSharedSystem.ZLevelVisualOffset);

        // Query entities on the map above and render them at offset
        var query = _entMan.EntityQueryEnumerator<TransformComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var xform, out var sprite))
        {
            if (xform.MapUid != aboveMap.Value.Owner)
                continue;

            var worldPos = transformSystem.GetWorldPosition(xform) + offset;
            var color = sprite.Color.WithAlpha(alpha);
            // #Cythisiax Note: Full entity rendering through openings requires
            // a more sophisticated approach. For now, draw position markers.
            args.WorldHandle.DrawCircle(worldPos, 0.3f, color);
        }
    }
}
