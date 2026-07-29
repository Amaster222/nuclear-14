// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;

namespace Content.Shared._Misfits.Interaction;

/// <summary>
/// Lets you ignore action blockers while conscious and interact with obstructed entities, if they are still in range.
/// </summary>
/// <remarks>
/// Not relayed to mutations and handled there because interaction is really, really common.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class TelekinesisComponent : Component
{
    /// <summary>
    /// How far away the holder can interact with things (open doors, pick up items, etc.)
    /// without touching them. Normal interaction range still applies to everything else.
    /// </summary>
    [DataField]
    public float Range = 8f;

    /// <summary>
    /// How hard a tethered object is hurled when the action is used on another target.
    /// </summary>
    [DataField]
    public float ThrowForce = 15f;
}

/// <summary>
/// Event for tethering the target entity.
/// </summary>
public sealed partial class TelekinesisActionEvent : EntityTargetActionEvent;
