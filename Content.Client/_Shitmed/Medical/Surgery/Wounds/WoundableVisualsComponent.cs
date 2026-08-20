// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Shitmed.Medical.Surgery.Wounds;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Humanoid;

namespace Content.Client._Shitmed.Medical.Surgery.Wounds;

/// <summary>
/// Client presentation data for a woundable body part. This is deliberately
/// separate from <c>DamageVisuals</c>: Shitmed wounds belong to an attached
/// limb and must continue to render correctly after detachment.
/// </summary>
[RegisterComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WoundableVisualsComponent : Component
{
    [DataField(required: true)]
    public HumanoidVisualLayers OccupiedLayer;

    [DataField]
    public Dictionary<string, WoundVisualizerSprite>? DamageOverlayGroups = new();

    [DataField]
    public string? BleedingOverlay;

    [DataField(required: true)]
    public List<FixedPoint2> Thresholds = [];

    [DataField]
    public Dictionary<BleedingSeverity, FixedPoint2> BleedingThresholds = new()
    {
        { BleedingSeverity.Minor, 2.6 },
        { BleedingSeverity.Severe, 7 },
    };
}

[DataDefinition]
public sealed partial class WoundVisualizerSprite
{
    [DataField(required: true)]
    public string Sprite = default!;

    [DataField]
    public string? Color;
}
