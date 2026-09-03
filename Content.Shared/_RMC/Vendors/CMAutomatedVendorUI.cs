using Robust.Shared.Serialization;

namespace Content.Shared._RMC.Vendors;

[Serializable, NetSerializable]
public enum CMAutomatedVendorUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorState : BoundUserInterfaceState
{
    public List<CMVendorSectionState> Sections { get; }
    public List<CMVendorStoredItemState> StoredItems { get; }
    public int Points { get; }
    public int ReplenishmentPoints { get; }
    public bool CanReplenish { get; }
    public bool CanStoreEquipment { get; }
    public string DepartmentName { get; }

    public CMAutomatedVendorState(
        List<CMVendorSectionState> sections,
        List<CMVendorStoredItemState> storedItems,
        int points,
        int replenishmentPoints,
        bool canReplenish,
        bool canStoreEquipment,
        string departmentName)
    {
        Sections = sections;
        StoredItems = storedItems;
        Points = points;
        ReplenishmentPoints = replenishmentPoints;
        CanReplenish = canReplenish;
        CanStoreEquipment = canStoreEquipment;
        DepartmentName = departmentName;
    }
}

[Serializable, NetSerializable]
public sealed record CMVendorSectionState(string Name, int? Choices, int Purchases, List<CMVendorEntryState> Entries);

[Serializable, NetSerializable]
public sealed record CMVendorEntryState(
    string Name,
    EntProtoId Id,
    int? Amount,
    int? Points,
    int Tier,
    bool HasAuthority,
    string? RequiredAuthority);

[Serializable, NetSerializable]
public sealed record CMVendorStoredItemState(string Name, EntProtoId Id);

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorVendMessage : BoundUserInterfaceMessage
{
    public int Section { get; }
    public int Entry { get; }

    public CMAutomatedVendorVendMessage(int section, int entry)
    {
        Section = section;
        Entry = entry;
    }
}

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorReplenishMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorStoreHeldMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CMAutomatedVendorWithdrawStoredMessage : BoundUserInterfaceMessage
{
    public int Index { get; }

    public CMAutomatedVendorWithdrawStoredMessage(int index)
    {
        Index = index;
    }
}
