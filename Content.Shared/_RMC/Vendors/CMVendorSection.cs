using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC.Vendors;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class CMVendorSection
{
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public (string Id, int Amount)? Choices;

    [DataField]
    public string? TakeAll;

    [DataField]
    public string? TakeOne;

    [DataField(required: true)]
    public List<CMVendorEntry> Entries = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record CMVendorEntry
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public string? Name;

    [DataField]
    public int? Amount;

    /// <summary>
    /// The finite stock ceiling used by faction replenishment. When omitted, the configured initial amount becomes
    /// the ceiling on first use.
    /// </summary>
    [DataField]
    public int? MaxAmount;

    [DataField]
    public int? Points;

    /// <summary>
    /// Shared faction replenishment points required to restore one unit of this entry. Falls back to the tier cost.
    /// </summary>
    [DataField]
    public int? ReplenishmentCost;

    /// <summary>
    /// Blueprint-style faction authority tier. Valid values are 1 through 4.
    /// </summary>
    [DataField]
    public int Tier = 1;

    [DataField]
    public List<EntProtoId> LinkedEntries = new();
}
