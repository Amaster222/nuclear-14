// #Cythisiax Add - Free market terminal component (shared)
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Trade.Market;

/// <summary>
/// Marker component for the Wendover Free Market terminal.
/// Interaction is handled by MarketSystem (server) and MarketBoundUi (client).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MarketTerminalComponent : Component
{
}

/// <summary>
/// UI key for the market terminal BUI.
/// </summary>
[Serializable, NetSerializable]
public enum MarketUiKey : byte
{
    Key
}

/// <summary>
/// A single listing's data, sent from server to client.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketListingData
{
    public string ListingId = string.Empty;
    public string SellerName = string.Empty;
    public string PrototypeId = string.Empty;
    public string PrototypeName = string.Empty;
    public int Quantity = 1;
    public int StackCount;
    public string Currency = string.Empty;
    public int PricePerUnit;
    public string? RequestedItemId;
    public int RequestedQuantity;
    public DateTime ListedAt;
    public DateTime ExpiresAt;
}

/// <summary>
/// Sent from client to server: request to list an item.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketListMessage(string prototypeId, int quantity, int stackCount, string currency, int pricePerUnit, string? requestedItemId = null, int requestedQuantity = 0) : BoundUserInterfaceMessage
{
    public string PrototypeId = prototypeId;
    public int Quantity = quantity;
    public int StackCount = stackCount;
    public string Currency = currency;
    public int PricePerUnit = pricePerUnit;
    public string? RequestedItemId = requestedItemId;
    public int RequestedQuantity = requestedQuantity;
}

/// <summary>
/// Sent from client to server: deposit held item into market storage.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketDepositItemMessage : BoundUserInterfaceMessage
{
}

/// <summary>
/// Sent from client to server: withdraw an item from market storage.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketWithdrawItemMessage(string slotKey) : BoundUserInterfaceMessage
{
    public string SlotKey = slotKey;
}

/// <summary>
/// Sent from server to client: held item info for listing form.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketHeldItemResponse(string? protoId = null, string? protoName = null, int stackCount = 0) : BoundUserInterfaceMessage
{
    public string? ProtoId = protoId;
    public string? ProtoName = protoName;
    public int StackCount = stackCount;
}

/// <summary>
/// A single item in the player's market deposit storage.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketDepositEntry
{
    public string SlotKey = string.Empty;
    public string ProtoId = string.Empty;
    public string ProtoName = string.Empty;
    public int StackCount;
    public int Quantity = 1;
}

/// <summary>
/// Per-item summary for the market overview (EVE-like).
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketItemSummary
{
    public string PrototypeId = string.Empty;
    public string PrototypeName = string.Empty;
    public int ListingCount;
    public int LowestPrice;
    public int HighestPrice;
    public string Currency = string.Empty;
}

/// <summary>
/// A single activity feed entry.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketFeedEntry
{
    public string Text = string.Empty;
    public DateTime Time;
}

/// <summary>
/// Sent from client to server: buy a listing.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketBuyMessage(string listingId, int quantity) : BoundUserInterfaceMessage
{
    public string ListingId = listingId;
    public int Quantity = quantity;
}

/// <summary>
/// Sent from server to client: full market state snapshot.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketStateMessage : BoundUserInterfaceState
{
    public List<MarketListingData> Listings = new();
    public List<MarketListingData> MyListings = new();
    public List<MarketFeedEntry> Feed = new(); // #Cythisiax Add - activity feed
    public List<MarketItemSummary> ItemSummaries = new(); // #Cythisiax Add - EVE-like overview
    public List<MarketDepositEntry> DepositedItems = new(); // #Cythisiax Add - player's deposit storage
    public string MarketName = "Wendover Free Market";
    // Currency balances for the viewing player
    public int Bottlecaps;
    public int NcrDollars;
    public int Silver;
    public int Gold;
}
