using Robust.Shared.Analyzers;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;


namespace Content.Shared.Item.ItemToggle.Components;

/// <summary>
/// Handles changes to GunComponent when the item is toggled.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(ItemToggleGunSystems)), AutoGenerateComponentState]
public sealed partial class ItemToggleGunComponent : Component
{
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite), DataField("InactiveWeaponFireRate")]
    public float InactiveWeaponFireRate = 1f;

    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite), DataField("ActivatedSpeedModifier")]
    public float ActivatedSpeedModifier = 0.1f;

    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite), DataField("ActivatedFireRate")]
    public float ActivatedFireRate = 8f;

}
