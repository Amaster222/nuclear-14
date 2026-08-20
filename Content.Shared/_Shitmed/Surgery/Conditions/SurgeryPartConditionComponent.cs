// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Body.Part;
using Robust.Shared.GameStates;

namespace Content.Shared._Shitmed.Medical.Surgery.Conditions;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryPartConditionComponent : Component
{
    [DataField]
    public HashSet<BodyPartType> Parts = [];

    /// <summary>
    /// Legacy singular form retained for the existing surgery prototypes.
    /// New content should use <see cref="Parts"/> so it can target more than one part type.
    /// </summary>
    [DataField("part")]
    public BodyPartType? Part
    {
        get => Parts.Count > 0 ? Parts.First() : null;
        set
        {
            if (value is { } part)
                Parts = [part];
        }
    }

    [DataField]
    public BodyPartSymmetry? Symmetry;

    [DataField]
    public bool Inverse;
}
