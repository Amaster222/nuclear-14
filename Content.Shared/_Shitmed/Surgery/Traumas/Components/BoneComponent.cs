using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Shitmed.Medical.Surgery.Traumas.Components;

[RegisterComponent, AutoGenerateComponentState, NetworkedComponent]
public sealed partial class BoneComponent : Component
{
    [AutoNetworkedField, ViewVariables]
    public EntityUid? BoneWoundable;

    [DataField, AutoNetworkedField, ViewVariables]
    public FixedPoint2 IntegrityCap = 60f;

    [DataField, AutoNetworkedField, ViewVariables]
    public FixedPoint2 BoneIntegrity = 60f;

    [AutoNetworkedField, ViewVariables]
    public BoneSeverity BoneSeverity = BoneSeverity.Normal;

    [DataField]
    public Dictionary<BoneSeverity, FixedPoint2> HealSeverityFloor = new()
    {
        { BoneSeverity.Normal, 0 },
        { BoneSeverity.Damaged, 5 },
        { BoneSeverity.Cracked, 10 },
        { BoneSeverity.Broken, 30 },
    };

    /// <summary>
    /// Legacy per-bone trauma chance adjustments. The active trauma pipeline
    /// currently reads these from wound inflicters, but preserving the field
    /// keeps existing bone prototype values valid for a later integration.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<TraumaTypePrototype>, FixedPoint2> TraumasChances = new();

    [DataField]
    public SoundSpecifier BoneBreakSound = new SoundCollectionSpecifier("BoneGone");
}
