using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// A temporary friction and acceleration modifier applied by a status effect.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(MovementModStatusSystem))]
public sealed partial class FrictionStatusEffectComponent : Component
{
    [DataField, AutoNetworkedField]
    public float FrictionModifier = 1f;

    [DataField, AutoNetworkedField]
    public float AccelerationModifier = 1f;
}
