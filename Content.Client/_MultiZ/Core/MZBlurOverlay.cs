// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
//   PR #1058 "Multi Z addition" & #1119 "Multi z fixes" by TheHellFireo
//   Based on Crystall Edge (crystallpunk-14) Multi-Z system
// Ported to misfits-14 _MultiZ/ — renamed &amp; adapted
// #Cythisiax Ported — Multi-Z level support for misfits-14

using System.Numerics;
using Content.Shared._MultiZ;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;

namespace Content.Client._MultiZ.Core;

/// <summary>
/// Blur overlay applied when looking up or down between Z-levels.
/// Renders a semi-transparent dark tint over the viewport edges.
/// </summary>
public sealed class MZBlurOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public MZBlurOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_cfg.GetCVar(MZCVars.BlurEnabled))
            return;

        var player = _player.LocalSession?.AttachedEntity;
        if (player == null)
            return;

        var handle = args.WorldHandle;
        var viewport = args.ViewportBounds;
        var strength = _cfg.GetCVar(MZCVars.BlurStrength);

        // Simple semi-transparent overlay for Z-level transition
        handle.DrawRect(new Box2(Vector2.Zero, viewport.Size), Color.Black.WithAlpha(0.3f * strength));
    }
}
