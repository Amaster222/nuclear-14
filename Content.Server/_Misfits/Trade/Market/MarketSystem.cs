// #Cythisiax Add - Wendover Free Market server system
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._Misfits.Currency.Components;
using Content.Shared._Misfits.Trade.Market;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Trade.Market;

/// <summary>
/// Server-side system for the Wendover Free Market terminal.
/// Handles listing, buying, storage, DB persistence, and UI state.
/// </summary>
public sealed class MarketSystem : EntitySystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ActorSystem _actor = default!;

    private ISawmill _log = default!;

    // In-memory cache of active listings keyed by ListingId (Guid string).
    // Populated from DB on server start, synced on every mutation.
    private readonly Dictionary<string, MarketListing> _activeListings = new();

    /// <summary>
    /// Set of players who currently have the market UI open.
    /// </summary>
    private readonly HashSet<EntityUid> _openMarketUis = new();

    // Internal container prefix for per-listing item storage.
    private const string ListingSlotPrefix = "market_slot_";
    // Player deposit storage container prefix.
    private const string DepositSlotPrefix = "market_dep_";

    // Purge timer — runs once per minute
    private float _purgeTimer;

    // Activity feed — round-scoped, max 50 entries
    private readonly List<MarketFeedEntry> _activityFeed = new();
    private const int MaxFeedEntries = 50;

    public override void Initialize()
    {
        base.Initialize();

        _log = Logger.GetSawmill("market");

        SubscribeLocalEvent<MarketTerminalComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<MarketTerminalComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<MarketTerminalComponent, BoundUIClosedEvent>(OnUiClosed);

        // Global item verb: "Deposit to Market" when near a terminal
        SubscribeLocalEvent<GetVerbsEvent<UtilityVerb>>(OnItemVerb);

        // Round lifecycle
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);

        Subs.BuiEvents<MarketTerminalComponent>(MarketUiKey.Key, subs =>
        {
            subs.Event<MarketListMessage>(OnListMessage);
            subs.Event<MarketBuyMessage>(OnBuyMessage);
            subs.Event<MarketDepositItemMessage>(OnDepositItem);
            subs.Event<MarketWithdrawItemMessage>(OnWithdrawItem);
        });

        // Load active listings from DB on startup
        LoadActiveListingsAsync();
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    private void OnGetVerbs(Entity<MarketTerminalComponent> ent,
        ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("market-verb-open"),
            Priority = 10,
            Act = () => OpenMarketForPlayer(user, ent),
        });
    }

    /// <summary>
    /// Add "Deposit to Market" verb on items when the user is near a market terminal.
    /// </summary>
    private void OnItemVerb(GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Target == args.User)
            return;

        var user = args.User;

        // Check if the user is near a market terminal
        var query = EntityQueryEnumerator<MarketTerminalComponent, TransformComponent>();
        while (query.MoveNext(out var terminalUid, out _, out var terminalXform))
        {
            var userXform = Transform(user);
            if (!terminalXform.Coordinates.InRange(EntityManager, userXform.Coordinates, 2f))
                continue;

            // Found a nearby terminal — add deposit verb for this item
            var item = args.Target;
            var terminal = terminalUid;

            args.Verbs.Add(new UtilityVerb
            {
                Text = Loc.GetString("market-verb-deposit"),
                Act = () => DepositItemIntoMarket(item, terminal, user),
            });

            break; // only add once
        }
    }

    private void OnActivate(Entity<MarketTerminalComponent> ent,
        ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        OpenMarketForPlayer(args.User, ent);
        args.Handled = true;
    }

    private void OpenMarketForPlayer(EntityUid user, Entity<MarketTerminalComponent> terminal)
    {
        if (!TryComp<ActorComponent>(user, out _))
            return;

        if (!_ui.IsUiOpen(terminal.Owner, MarketUiKey.Key, user))
            _ui.OpenUi(terminal.Owner, MarketUiKey.Key, user);

        _openMarketUis.Add(user);

        RefreshMarketState(terminal);
    }

    private void OnUiClosed(Entity<MarketTerminalComponent> ent, ref BoundUIClosedEvent args)
    {
        _openMarketUis.Remove(args.Actor);
    }

    // ── Listing flow (Phase 2) ────────────────────────────────────────────────

    private void OnListMessage(Entity<MarketTerminalComponent> terminal, ref MarketListMessage msg)
    {
        // Get the user who sent the message
        if (!TryComp<ActorComponent>(terminal, out var terminalActor))
            return;

        var session = terminalActor.PlayerSession;
        var user = session.AttachedEntity;
        if (user == null || user == EntityUid.Invalid)
            return;

        var userId = session.UserId;
        var sellerCharName = session.Name;

        // ── Validate: item exists in player's possession ──────────────────
        if (!TryFindItemInPossession(user.Value, msg.PrototypeId, out var itemEnt))
        {
            _log.Debug($"List rejected: {sellerCharName} doesn't have {msg.PrototypeId}");
            return;
        }

        // ── Validate: max 3 active listings per player ────────────────────
        var myActive = _activeListings.Values
            .Count(l => l.SellerPlayerId == userId.UserId && l.Status == "Active");
        if (myActive >= 3)
        {
            _log.Debug($"List rejected: {sellerCharName} already has {myActive} active listings");
            return;
        }

        // ── Validate: listing fee affordable ──────────────────────────────
        var fee = CalculateFee(msg.Currency, msg.PricePerUnit);
        if (fee > 0 && !TryDeductFee(user.Value, msg.Currency, fee))
        {
            _log.Debug($"List rejected: {sellerCharName} can't afford {fee} listing fee");
            return;
        }

        // ── Create listing ────────────────────────────────────────────────
        var listingId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var dbListing = new MarketListing
        {
            ListingId = listingId,
            SellerPlayerId = userId.UserId,
            SellerCharacterName = sellerCharName,
            PrototypeId = msg.PrototypeId,
            Quantity = msg.Quantity,
            StackCount = msg.StackCount,
            Currency = msg.Currency,
            PricePerUnit = msg.PricePerUnit,
            RequestedItemId = msg.RequestedItemId,
            RequestedQuantity = msg.RequestedQuantity,
            ListedAt = now,
            ExpiresAt = now.AddDays(3),
            Status = "Active",
        };

        // ── Move item into terminal storage ───────────────────────────────
        var slotName = $"{ListingSlotPrefix}{listingId}";
        var slot = _container.EnsureContainer<ContainerSlot>(terminal.Owner, slotName);
        if (!_container.Insert(itemEnt, slot))
        {
            _log.Error($"Failed to insert listed item {itemEnt} into container {slotName}");
            return;
        }

        // ── Persist to DB ─────────────────────────────────────────────────
        _activeListings[listingId.ToString()] = dbListing;
        _ = _db.UpsertMarketListingAsync(dbListing);

        _log.Info($"Market list: {sellerCharName} listed {msg.Quantity}x {msg.PrototypeId} " +
                  $"for {msg.PricePerUnit} {msg.Currency} (fee {fee})");

        // Push activity feed
        PushFeed($"{sellerCharName} listed {msg.PrototypeId} x{msg.Quantity} for {msg.PricePerUnit} {msg.Currency}");

        // Broadcast updated state to all open UIs
        foreach (var openUser in _openMarketUis.ToList())
        {
            if (TryComp<MarketTerminalComponent>(terminal, out var _))
                RefreshMarketState(terminal);
        }
    }

    // ── Buy flow (Phase 3) ────────────────────────────────────────────────────

    private void OnBuyMessage(Entity<MarketTerminalComponent> terminal, ref MarketBuyMessage msg)
    {
        // Get the buyer
        if (!TryComp<ActorComponent>(terminal, out var terminalActor))
            return;

        var session = terminalActor.PlayerSession;
        var buyer = session.AttachedEntity;
        if (buyer == null || buyer == EntityUid.Invalid)
            return;

        var buyerId = session.UserId;
        var buyerName = session.Name;

        // Find the listing
        if (!_activeListings.TryGetValue(msg.ListingId, out var listing) || listing.Status != "Active")
        {
            _log.Debug($"Buy rejected: listing {msg.ListingId} not found or not active");
            return;
        }

        // Don't buy your own listing
        if (listing.SellerPlayerId == buyerId.UserId)
        {
            _log.Debug($"Buy rejected: {buyerName} tried to buy own listing");
            return;
        }

        // ── Handle payment ─────────────────────────────────────────────────
        if (listing.Currency == "Barter")
        {
            // Validate buyer has the requested barter item
            if (string.IsNullOrEmpty(listing.RequestedItemId))
            {
                _log.Debug($"Buy rejected: invalid barter listing {msg.ListingId}");
                return;
            }

            if (!TryFindItemInPossession(buyer.Value, listing.RequestedItemId, out var barterItem))
            {
                _log.Debug($"Buy rejected: {buyerName} doesn't have {listing.RequestedItemId} for barter");
                return;
            }

            // Consume the barter item
            QueueDel(barterItem);
        }
        else
        {
            // Currency payment
            var totalPrice = listing.PricePerUnit * msg.Quantity;
            var fee = CalculateFee(listing.Currency, totalPrice);
            var sellerProceeds = totalPrice - fee;

            if (fee <= 0 && totalPrice > 0)
                return; // invalid

            // Deduct from buyer
            if (!TryDeductFee(buyer.Value, listing.Currency, totalPrice))
            {
                _log.Debug($"Buy rejected: {buyerName} can't afford {totalPrice} {listing.Currency}");
                return;
            }

            // Tax sink (the fee is already deducted from buyer; the "trashbin" is the server)
            // 10% fee is just gone — not credited to anyone
            _log.Info($"Market: {buyerName} bought {listing.PrototypeId} from {listing.SellerCharacterName} " +
                      $"for {totalPrice} {listing.Currency} (fee {fee}, seller gets {sellerProceeds})");

            // Credit seller's persistent balance (may be offline)
            _ = CreditSellerAsync(listing.SellerPlayerId, listing.SellerCharacterName,
                listing.Currency, sellerProceeds);
        }

        // ── Deliver item to buyer ──────────────────────────────────────────
        var slotName = $"{ListingSlotPrefix}{listing.ListingId}";
        if (_container.TryGetContainer(terminal.Owner, slotName, out var container) &&
            container is ContainerSlot buySlot && buySlot.ContainedEntity is { } storedItem)
        {
            _container.Remove(storedItem, buySlot);

            // Try to put in buyer's hands, or at their feet
            if (!_hands.TryPickupAnyHand(buyer.Value, storedItem))
            {
                _xform.DropNextTo(storedItem, buyer.Value);
            }
        }

        // ── Mark listing as sold ───────────────────────────────────────────
        listing.Status = "Sold";
        listing.SoldToCharacter = buyerName;
        listing.SoldAt = DateTime.UtcNow;
        listing.SoldItemTag = $"market-sold-{listing.ListingId}";

        // Persist
        _ = _db.UpsertMarketListingAsync(listing);
        _ = _db.AddMarketSoldItemAsync(listing.SoldItemTag);
        _ = _db.AddMarketSaleAsync(new MarketSale
        {
            ListingId = listing.ListingId,
            ItemProto = listing.PrototypeId,
            Price = listing.PricePerUnit * msg.Quantity,
            Currency = listing.Currency,
            SellerId = listing.SellerPlayerId,
            SellerName = listing.SellerCharacterName,
            BuyerId = buyerId.UserId,
            BuyerName = buyerName,
            SoldAt = listing.SoldAt ?? DateTime.UtcNow,
        });

        // Push activity feed
        var priceDisplay = listing.Currency == "Barter"
            ? $"for {listing.RequestedItemId ?? "?"}"
            : $"for {listing.PricePerUnit * msg.Quantity} {listing.Currency}";
        PushFeed($"{buyerName} bought {listing.PrototypeId} from {listing.SellerCharacterName} {priceDisplay}");

        // Broadcast updated state
        RefreshMarketState(terminal);
    }

    // ── Deposit / Withdraw (player-private storage) ────────────────────────────

    /// <summary>
    /// Deposit an item into the player's market storage via right-click verb.
    /// </summary>
    private void DepositItemIntoMarket(EntityUid item, EntityUid terminal, EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var userId = actor.PlayerSession.UserId.UserId;

        // Find an unused slot index
        var slotIdx = 0;
        string slotName;
        do
        {
            slotName = $"{DepositSlotPrefix}{userId}_{slotIdx}";
            slotIdx++;
        } while (_container.TryGetContainer(terminal, slotName, out _));

        var slot = _container.EnsureContainer<ContainerSlot>(terminal, slotName);
        if (!_container.Insert(item, slot))
            return;

        _log.Debug($"Market verb deposit: {actor.PlayerSession.Name} deposited {MetaData(item).EntityPrototype?.ID}");

        // Refresh any open UI for this user
        foreach (var openUser in _openMarketUis.ToList())
        {
            if (openUser == user && TryComp<MarketTerminalComponent>(terminal, out _))
                RefreshMarketState((terminal, Comp<MarketTerminalComponent>(terminal)));
        }
    }

    private void OnDepositItem(Entity<MarketTerminalComponent> terminal, ref MarketDepositItemMessage msg)
    {
        if (!TryComp<ActorComponent>(terminal, out var terminalActor))
            return;

        var user = terminalActor.PlayerSession.AttachedEntity;
        if (user == null || user == EntityUid.Invalid)
            return;

        var userId = terminalActor.PlayerSession.UserId.UserId;

        // Find an item in the player's active hand
        EntityUid? heldItem = null;
        foreach (var held in _hands.EnumerateHeld(user.Value))
        {
            heldItem = held;
            break;
        }

        if (heldItem == null)
            return;

        // Create a deposit slot keyed to this player
        var slotIdx = 0;
        string slotName;
        do
        {
            slotName = $"{DepositSlotPrefix}{userId}_{slotIdx}";
            slotIdx++;
        } while (_container.TryGetContainer(terminal.Owner, slotName, out _));

        var slot = _container.EnsureContainer<ContainerSlot>(terminal.Owner, slotName);
        if (!_container.Insert(heldItem.Value, slot))
            return;

        RefreshMarketState(terminal);
    }

    private void OnWithdrawItem(Entity<MarketTerminalComponent> terminal, ref MarketWithdrawItemMessage msg)
    {
        if (!TryComp<ActorComponent>(terminal, out var terminalActor))
            return;

        var user = terminalActor.PlayerSession.AttachedEntity;
        if (user == null || user == EntityUid.Invalid)
            return;

        // Only allow withdrawing your own items
        var userId = terminalActor.PlayerSession.UserId.UserId;
        if (!msg.SlotKey.StartsWith($"{DepositSlotPrefix}{userId}_"))
            return;

        if (!_container.TryGetContainer(terminal.Owner, msg.SlotKey, out var container)
            || container is not ContainerSlot slot
            || slot.ContainedEntity is not { } item)
            return;

        _container.Remove(item, slot);

        if (!_hands.TryPickupAnyHand(user.Value, item))
            _xform.DropNextTo(item, user.Value);

        RefreshMarketState(terminal);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Find an entity of the given prototype in the player's hands or named inventory containers.
    /// </summary>
    private bool TryFindItemInPossession(EntityUid user, string prototypeId, out EntityUid item)
    {
        item = EntityUid.Invalid;

        // Check hands
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (MetaData(held).EntityPrototype?.ID == prototypeId)
            {
                item = held;
                return true;
            }
        }

        // Check named inventory slots
        var slotNames = new[] { "jumpsuit", "back", "pocket1", "pocket2", "outerClothing", "belt", "shoes", "gloves", "neck", "mask", "eyes", "ears", "head", "id" };
        foreach (var slotName in slotNames)
        {
            if (_container.TryGetContainer(user, slotName, out var container))
            {
                foreach (var contained in container.ContainedEntities)
                {
                    if (MetaData(contained).EntityPrototype?.ID == prototypeId)
                    {
                        item = contained;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Calculate the 10% listing/buy fee for a currency transaction, rounded down.
    /// </summary>
    private static int CalculateFee(string currency, int pricePerUnit)
    {
        if (currency == "Barter")
            return 0;

        return Math.Max(0, pricePerUnit / 10); // 10% rounded down (floor)
    }

    /// <summary>
    /// Deduct the listing fee from the player's persistent balance.
    /// Also supports paying from held currency stacks in future phases.
    /// </summary>
    private bool TryDeductFee(EntityUid user, string currency, int amount)
    {
        if (amount <= 0)
            return true;

        // Find the matching CurrencyType
        var curType = currency switch
        {
            "Bottlecaps" => CurrencyType.Bottlecaps,
            "NCRDollars" => CurrencyType.NCRDollars,
            "Silver" => CurrencyType.Silver,
            "Gold" => CurrencyType.Gold,
            _ => (CurrencyType?)null,
        };

        if (curType == null)
            return false;

        if (!TryComp<PersistentCurrencyComponent>(user, out var wallet))
            return false;

        // Check balance
        var balance = GetCurrencyBalance(wallet, curType.Value);
        if (balance < amount)
            return false;

        // Deduct
        SetCurrencyBalance(wallet, curType.Value, balance - amount);
        Dirty(user, wallet);

        // Save to DB (fire and forget)
        if (wallet.UserId != null && wallet.CharacterName != null &&
            Guid.TryParse(wallet.UserId, out var playerId))
        {
            _ = _db.UpsertCharacterCurrencyAsync(playerId, wallet.CharacterName,
                wallet.Bottlecaps, wallet.NcrDollars, wallet.Silver, wallet.Gold);
        }

        return true;
    }

    private static int GetCurrencyBalance(PersistentCurrencyComponent wallet, CurrencyType type)
    {
        return type switch
        {
            CurrencyType.Bottlecaps => wallet.Bottlecaps,
            CurrencyType.NCRDollars => wallet.NcrDollars,
            CurrencyType.Silver => wallet.Silver,
            CurrencyType.Gold => wallet.Gold,
            _ => 0,
        };
    }

    private static void SetCurrencyBalance(PersistentCurrencyComponent wallet, CurrencyType type, int value)
    {
        switch (type)
        {
            case CurrencyType.Bottlecaps: wallet.Bottlecaps = value; break;
            case CurrencyType.NCRDollars: wallet.NcrDollars = value; break;
            case CurrencyType.Silver: wallet.Silver = value; break;
            case CurrencyType.Gold: wallet.Gold = value; break;
        }
    }

    /// <summary>
    /// Credit a seller's persistent balance after a sale. Handles offline sellers
    /// by reading current balance from DB, adding proceeds, and upserting.
    /// </summary>
    private async Task CreditSellerAsync(Guid sellerId, string sellerCharName, string currency, int amount)
    {
        if (amount <= 0)
            return;

        try
        {
            var row = await _db.GetCharacterCurrencyAsync(sellerId, sellerCharName);
            var caps = row?.Bottlecaps ?? 0;
            var ncr = row?.NcrDollars ?? 0;
            var silver = row?.Silver ?? 0;
            var gold = row?.Gold ?? 0;

            switch (currency)
            {
                case "Bottlecaps": caps += amount; break;
                case "NCRDollars": ncr += amount; break;
                case "Silver": silver += amount; break;
                case "Gold": gold += amount; break;
            }

            await _db.UpsertCharacterCurrencyAsync(sellerId, sellerCharName, caps, ncr, silver, gold);

            // Also update in-memory if seller is online
            var query = EntityQueryEnumerator<PersistentCurrencyComponent>();
            while (query.MoveNext(out var uid, out var wallet))
            {
                if (wallet.UserId == sellerId.ToString() && wallet.CharacterName == sellerCharName)
                {
                    SetCurrencyBalance(wallet, currency switch
                    {
                        "Bottlecaps" => CurrencyType.Bottlecaps,
                        "NCRDollars" => CurrencyType.NCRDollars,
                        "Silver" => CurrencyType.Silver,
                        "Gold" => CurrencyType.Gold,
                        _ => CurrencyType.Bottlecaps,
                    }, currency switch
                    {
                        "Bottlecaps" => caps,
                        "NCRDollars" => ncr,
                        "Silver" => silver,
                        "Gold" => gold,
                        _ => caps,
                    });
                    Dirty(uid, wallet);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to credit seller {sellerCharName}: {ex}");
        }
    }

    // ── Activity feed ──────────────────────────────────────────────────────────

    private void PushFeed(string text)
    {
        _activityFeed.Insert(0, new MarketFeedEntry { Text = text, Time = DateTime.UtcNow });
        if (_activityFeed.Count > MaxFeedEntries)
            _activityFeed.RemoveAt(_activityFeed.Count - 1);
    }

    // ── State broadcast ───────────────────────────────────────────────────────

    // ── Round lifecycle ────────────────────────────────────────────────────────

    private async void LoadActiveListingsAsync()
    {
        try
        {
            var listings = await _db.GetActiveMarketListingsAsync();
            foreach (var listing in listings)
                _activeListings[listing.ListingId.ToString()] = listing;

            _log.Info($"Loaded {_activeListings.Count} active market listings from DB.");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load active market listings: {ex}");
        }
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        // Clear stale state from previous round
        _activeListings.Clear();
        _openMarketUis.Clear();
        _activityFeed.Clear();

        // Re-materialize stored items for active listings from DB
        ReMaterializeListingsAsync();
    }

    private async void ReMaterializeListingsAsync()
    {
        try
        {
            var listings = await _db.GetActiveMarketListingsAsync();
            _log.Info($"Re-materializing {listings.Count} market listings after round start.");

            // Find a market terminal to store items in
            var query = EntityQueryEnumerator<MarketTerminalComponent>();
            EntityUid? terminal = null;
            while (query.MoveNext(out var uid, out _))
            {
                terminal = uid;
                break;
            }

            if (terminal == null)
            {
                _log.Warning("No market terminal found for re-materialization.");
                return;
            }

            foreach (var listing in listings)
            {
                _activeListings[listing.ListingId.ToString()] = listing;

                // Spawn the stored item from prototype and put it in a container
                if (!_proto.HasIndex<EntityPrototype>(listing.PrototypeId))
                {
                    _log.Warning($"Market listing prototype '{listing.PrototypeId}' no longer exists — skipping.");
                    continue;
                }

                var spawnCoords = Transform(terminal.Value).Coordinates;
                var spawned = Spawn(listing.PrototypeId, spawnCoords);
                if (listing.StackCount > 0 && TryComp<StackComponent>(spawned, out var stack))
                    _stack.SetCount(spawned, listing.StackCount, stack);

                var slotName = $"{ListingSlotPrefix}{listing.ListingId}";
                var slot = _container.EnsureContainer<ContainerSlot>(terminal.Value, slotName);
                if (!_container.Insert(spawned, slot))
                {
                    _log.Warning($"Failed to re-insert listing {listing.ListingId} item into container.");
                    QueueDel(spawned);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to re-materialize market listings: {ex}");
        }
    }

    // ── Update / purge ─────────────────────────────────────────────────────────

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _purgeTimer += frameTime;
        if (_purgeTimer >= 60f) // every 60 seconds
        {
            _purgeTimer = 0f;
            PurgeExpiredListingsAsync();
        }
    }

    private async void PurgeExpiredListingsAsync()
    {
        try
        {
            await _db.DeleteExpiredMarketListingsAsync();

            // Remove purged listings from in-memory cache
            var now = DateTime.UtcNow;
            var purged = _activeListings.Values
                .Where(l => l.Status == "Active" && l.ExpiresAt < now)
                .ToList();

            foreach (var listing in purged)
            {
                listing.Status = "Purged";

                // Destroy the stored item
                var slotName = $"{ListingSlotPrefix}{listing.ListingId}";
                var query = EntityQueryEnumerator<MarketTerminalComponent>();
                while (query.MoveNext(out var uid, out _))
                {
                    if (_container.TryGetContainer(uid, slotName, out var container) &&
                        container is ContainerSlot purgeSlot && purgeSlot.ContainedEntity is { } purgeItem)
                    {
                        QueueDel(purgeItem);
                    }
                }
            }

            if (purged.Count > 0)
                _log.Info($"Purged {purged.Count} expired market listings.");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to purge expired listings: {ex}");
        }
    }

    private void RefreshMarketState(Entity<MarketTerminalComponent> terminal)
    {
        foreach (var user in _openMarketUis.ToList())
        {
            if (!_ui.IsUiOpen(terminal.Owner, MarketUiKey.Key, user))
                continue;

            var state = BuildMarketState(terminal, user);
            _ui.SetUiState(terminal.Owner, MarketUiKey.Key, state);
        }
    }

    private MarketStateMessage BuildMarketState(Entity<MarketTerminalComponent> terminal, EntityUid user)
    {
        var active = _activeListings.Values.Where(l => l.Status == "Active").ToList();

        var listingData = active.Select(l =>
        {
            var name = _proto.TryIndex<EntityPrototype>(l.PrototypeId, out var proto)
                ? proto.Name : l.PrototypeId;

            return new MarketListingData
            {
                ListingId = l.ListingId.ToString(),
                SellerName = l.SellerCharacterName,
                PrototypeId = l.PrototypeId,
                PrototypeName = name,
                Quantity = l.Quantity,
                StackCount = l.StackCount,
                Currency = l.Currency,
                PricePerUnit = l.PricePerUnit,
                RequestedItemId = l.RequestedItemId,
                RequestedQuantity = l.RequestedQuantity,
                ListedAt = l.ListedAt,
                ExpiresAt = l.ExpiresAt,
            };
        }).ToList();

        Guid? userId = null;
        TryGetMarketPlayerInfo(user, out userId, out var charName);

        var myListings = active
            .Where(l => l.SellerPlayerId == userId)
            .Select(l =>
            {
                var name = _proto.TryIndex<EntityPrototype>(l.PrototypeId, out var proto)
                    ? proto.Name : l.PrototypeId;

                return new MarketListingData
                {
                    ListingId = l.ListingId.ToString(),
                    SellerName = l.SellerCharacterName,
                    PrototypeId = l.PrototypeId,
                    PrototypeName = name,
                    Quantity = l.Quantity,
                    StackCount = l.StackCount,
                    Currency = l.Currency,
                    PricePerUnit = l.PricePerUnit,
                    RequestedItemId = l.RequestedItemId,
                    RequestedQuantity = l.RequestedQuantity,
                    ListedAt = l.ListedAt,
                    ExpiresAt = l.ExpiresAt,
                };
            }).ToList();

        var state = new MarketStateMessage
        {
            Listings = listingData,
            MyListings = myListings,
            Feed = new List<MarketFeedEntry>(_activityFeed),
            ItemSummaries = BuildItemSummaries(active),
            DepositedItems = BuildDepositedItems(terminal, user),
        };

        // Attach currency balances for the viewer
        if (TryComp<PersistentCurrencyComponent>(user, out var wallet))
        {
            state.Bottlecaps = wallet.Bottlecaps;
            state.NcrDollars = wallet.NcrDollars;
            state.Silver = wallet.Silver;
            state.Gold = wallet.Gold;
        }

        return state;
    }

    private bool TryGetMarketPlayerInfo(EntityUid user, out Guid? userId, out string? charName)
    {
        userId = null;
        charName = null;

        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        userId = actor.PlayerSession.UserId.UserId;
        charName = actor.PlayerSession.Name;
        return true;
    }

    private List<MarketItemSummary> BuildItemSummaries(List<MarketListing> active)
    {
        var groups = active
            .GroupBy(l => l.PrototypeId)
            .Select(g => new
            {
                ProtoId = g.Key,
                Name = _proto.TryIndex<EntityPrototype>(g.Key, out var p) ? p.Name : g.Key,
                Count = g.Count(),
                Lowest = g.Where(l => l.Currency != "Barter").Select(l => l.PricePerUnit).DefaultIfEmpty(0).Min(),
                Highest = g.Where(l => l.Currency != "Barter").Select(l => l.PricePerUnit).DefaultIfEmpty(0).Max(),
                Currency = g.First().Currency,
            })
            .Select(g => new MarketItemSummary
            {
                PrototypeId = g.ProtoId,
                PrototypeName = g.Name,
                ListingCount = g.Count,
                LowestPrice = g.Lowest,
                HighestPrice = g.Highest,
                Currency = g.Currency == "Barter" ? "Barter" : g.Currency,
            })
            .OrderByDescending(s => s.ListingCount)
            .ToList();

        return groups;
    }

    private List<MarketDepositEntry> BuildDepositedItems(Entity<MarketTerminalComponent> terminal, EntityUid user)
    {
        var entries = new List<MarketDepositEntry>();

        if (!TryComp<ActorComponent>(user, out var actor))
            return entries;

        var userId = actor.PlayerSession.UserId.UserId;
        var prefix = $"{DepositSlotPrefix}{userId}_";

        // Try slots numbered 0..99 (reasonable max deposits per player)
        for (var idx = 0; idx < 100; idx++)
        {
            var slotName = $"{DepositSlotPrefix}{userId}_{idx}";
            if (!_container.TryGetContainer(terminal.Owner, slotName, out var container)
                || container is not ContainerSlot slot
                || slot.ContainedEntity is not { } item)
                continue;

            var meta = MetaData(item);
            var protoId = meta.EntityPrototype?.ID ?? "";
            var protoName = meta.EntityPrototype?.Name ?? meta.EntityName;
            var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : 0;

            entries.Add(new MarketDepositEntry
            {
                SlotKey = slotName,
                ProtoId = protoId,
                ProtoName = protoName,
                StackCount = stackCount,
                Quantity = stackCount > 0 ? stackCount : 1,
            });
        }

        return entries;
    }
}

