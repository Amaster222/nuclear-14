using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.Cleanup;

/// <summary>
/// Marks a timed despawn added by <see cref="MisfitsWorldCleanupSystem"/> to a dropped body part or organ.
/// </summary>
/// <remarks>
/// This lets surgery clear only the cleanup timer when a part is attached to a body, without affecting
/// a timer deliberately supplied by an entity prototype or another system.
/// </remarks>
[RegisterComponent]
public sealed partial class GibletCleanupComponent : Component;
