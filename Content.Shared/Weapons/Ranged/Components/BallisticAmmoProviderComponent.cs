using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BallisticAmmoProviderComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public SoundSpecifier? SoundRack = new SoundPathSpecifier("/Audio/Weapons/Guns/Cock/smg_cock.ogg");

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public SoundSpecifier? SoundInsert = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/bullet_insert.ogg");

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public EntProtoId? Proto;

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public int Capacity = 30;
    public int Count => UnspawnedCount + Container.ContainedEntities.Count;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public int UnspawnedCount;

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public EntityWhitelist? Whitelist;

    [ViewVariables(VVAccess.ReadOnly)]
    public Container Container = default!;

    /// <summary>
    /// Used as a middle-man ???
    /// </summary>
    /// <remarks>
    /// Set to false for entities like turrets to avoid users being able to cycle them.
    /// </remarks>

    // #Misfit change: Killed this field with my bare hands
    // [DataField, AutoNetworkedField]
    // public List<EntityUid> Entities = new();

    /// <summary>
    /// Is the magazine allowed to be manually cycled to eject a cartridge.
    /// </summary>
    /// <remarks>
    /// Set to false for entities like turrets to avoid users being able to cycle them.
    /// </remarks>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public bool Cycleable = true;

    /// <summary>
    /// Is it okay for this entity to directly transfer its valid ammunition into another provider?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool MayTransfer;

    /// <summary>
    /// DoAfter delay for filling a bullet into another ballistic ammo provider.
    /// </summary>
    [DataField]
    public TimeSpan FillDelay = TimeSpan.FromSeconds(0.5);
}
