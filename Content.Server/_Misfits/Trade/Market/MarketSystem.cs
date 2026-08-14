// #Cythisiax Add - Wendover Free Market Exchange server system
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Stack;
using Content.Shared._Misfits.Currency.Components;
using Content.Shared._Misfits.Trade.Market;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Trade.Market;

public sealed class MarketSystem : EntitySystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ActorSystem _actor = default!;

    private ISawmill _log = default!;
    private readonly Dictionary<string, MarketOrder> _activeOrders = new();
    private readonly Dictionary<(Guid, string), int> _escrowCurrency = new();
    private readonly Dictionary<(Guid, string), (string ProtoId, int Qty)> _escrowItems = new();
    private readonly HashSet<EntityUid> _openMarketUis = new();
    private const string ListingSlotPrefix = "market_slot_";
    private float _purgeTimer;
    private readonly List<MarketFeedEntry> _activityFeed = new();
    private const int MaxFeedEntries = 50;
    private readonly Dictionary<Guid, string> _selectedProtoByUser = new();
    // #Cythisiax Add - Search results are tracked per player so one buyer's
    // search does not overwrite another player's UI state.
    private readonly Dictionary<Guid, (string Query, List<(string Id, string Name)> Results)> _searchResultsByUser = new();

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("market");
        SubscribeLocalEvent<MarketTerminalComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<MarketTerminalComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<MarketTerminalComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<GetVerbsEvent<UtilityVerb>>(OnItemVerb);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        // Refresh market UIs when deposit storage contents change (grid drag-drop)
        SubscribeLocalEvent<EntInsertedIntoContainerMessage>(OnDepositContainerChanged);
        SubscribeLocalEvent<EntRemovedFromContainerMessage>(OnDepositContainerChanged);
        Subs.BuiEvents<MarketTerminalComponent>(MarketUiKey.Key, subs =>
        {
            subs.Event<CreateOrderMessage>(OnCreateOrder);
            subs.Event<CancelOrderMessage>(OnCancelOrder);
            subs.Event<ClaimEscrowMessage>(OnClaimEscrow);
            subs.Event<MarketWithdrawItemMessage>(OnWithdrawItem);
            subs.Event<ProtoSearchMessage>(OnProtoSearch);
            subs.Event<SelectOrderBookMessage>(OnSelectOrderBook);
        });
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    private void OnGetVerbs(Entity<MarketTerminalComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess) return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString("market-verb-open"), Priority = 10, Act = () => OpenMarketForPlayer(user, ent) });
        args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString("market-verb-storage"), Priority = 9, Act = () => OpenDepositStorage(user, ent) });
    }

    private void OnItemVerb(GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Target == args.User) return;
        var user = args.User;
        var query = EntityQueryEnumerator<MarketTerminalComponent, TransformComponent>();
        while (query.MoveNext(out var terminalUid, out _, out var terminalXform))
        {
            if (!terminalXform.Coordinates.InRange(EntityManager, Transform(user).Coordinates, 2f)) continue;
            args.Verbs.Add(new UtilityVerb { Text = Loc.GetString("market-verb-deposit"), Act = () => DepositItemIntoMarket(args.Target, terminalUid, user) });
            break;
        }
    }

    private void OnActivate(Entity<MarketTerminalComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex) return;
        OpenMarketForPlayer(args.User, ent);
        args.Handled = true;
    }

    private void OpenMarketForPlayer(EntityUid user, Entity<MarketTerminalComponent> terminal)
    {
        if (!TryComp<ActorComponent>(user, out _)) return;
        if (!_ui.IsUiOpen(terminal.Owner, MarketUiKey.Key, user))
            _ui.OpenUi(terminal.Owner, MarketUiKey.Key, user);
        _openMarketUis.Add(user);
        RefreshMarketState(terminal);
    }

    private void OnUiClosed(Entity<MarketTerminalComponent> ent, ref BoundUIClosedEvent args) =>
        _openMarketUis.Remove(args.Actor);

    /// <summary>
    /// When a deposit storage container changes (grid drag-drop), refresh that player's market UI.
    /// </summary>
    private void OnDepositContainerChanged(EntInsertedIntoContainerMessage ev)
    {
        var containerEntity = ev.Container.Owner;
        var terminalQuery = EntityQueryEnumerator<MarketTerminalComponent>();
        while (terminalQuery.MoveNext(out var terminalUid, out var comp))
        {
            foreach (var (playerId, storage) in comp.PlayerStorage)
            {
                if (storage != containerEntity) continue;
                RefreshMarketState((terminalUid, comp));
                return;
            }
        }
    }

    private void OnDepositContainerChanged(EntRemovedFromContainerMessage ev)
    {
        var containerEntity = ev.Container.Owner;
        var terminalQuery = EntityQueryEnumerator<MarketTerminalComponent>();
        while (terminalQuery.MoveNext(out var terminalUid, out var comp))
        {
            foreach (var (playerId, storage) in comp.PlayerStorage)
            {
                if (storage != containerEntity) continue;
                RefreshMarketState((terminalUid, comp));
                return;
            }
        }
    }

    private void OnStackCountChanged(Entity<StackComponent> ent, ref StackCountChangedEvent args)
    {
        if (_openMarketUis.Count == 0)
            return;

        // #Cythisiax Add - Only refresh when a stack changes inside market storage.
        if (!_container.TryGetContainingContainer((ent.Owner, null, null), out var container))
            return;

        var containerEntity = container.Owner;
        var terminalQuery = EntityQueryEnumerator<MarketTerminalComponent>();
        while (terminalQuery.MoveNext(out var terminalUid, out var comp))
        {
            foreach (var (_, storage) in comp.PlayerStorage)
            {
                if (storage != containerEntity)
                    continue;

                RefreshMarketState((terminalUid, comp));
                return;
            }
        }
    }

    // ── Deposit Storage ───────────────────────────────────────────────────────

    private EntityUid GetOrCreateDepositStorage(EntityUid terminal, EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor)) return EntityUid.Invalid;
        var userId = actor.PlayerSession.UserId.UserId;
        var comp = Comp<MarketTerminalComponent>(terminal);
        if (comp.PlayerStorage.TryGetValue(userId, out var existing) && Exists(existing)) return existing;
        var storage = Spawn("MarketDepositStorage", Transform(terminal).Coordinates);
        comp.PlayerStorage[userId] = storage;
        Dirty(terminal, comp);
        return storage;
    }

    private void OpenDepositStorage(EntityUid user, Entity<MarketTerminalComponent> terminal)
    {
        if (!TryComp<ActorComponent>(user, out var actor)) return;
        var storage = GetOrCreateDepositStorage(terminal.Owner, user);
        if (storage == EntityUid.Invalid) return;
        _ui.OpenUi(storage, StorageComponent.StorageUiKey.Key, actor.PlayerSession);
    }

    private void DepositItemIntoMarket(EntityUid item, EntityUid terminal, EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out _)) return;
        var storage = GetOrCreateDepositStorage(terminal, user);
        if (storage == EntityUid.Invalid || !TryComp<StorageComponent>(storage, out var sc)) return;
        if (!_container.Insert(item, sc.Container)) return;
        if (_openMarketUis.Contains(user)) RefreshMarketState((terminal, Comp<MarketTerminalComponent>(terminal)));
    }

    private void OnWithdrawItem(Entity<MarketTerminalComponent> terminal, ref MarketWithdrawItemMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        var user = msg.Actor;
        var comp = terminal.Comp;
        if (!comp.PlayerStorage.TryGetValue(actor.PlayerSession.UserId.UserId, out var storage) || !Exists(storage)
            || !TryComp<StorageComponent>(storage, out var sc)) return;
        EntityUid? toRemove = null;
        foreach (var c in sc.Container.ContainedEntities)
        {
            if (c.ToString() != msg.SlotKey)
                continue;

            toRemove = c;
            break;
        }

        if (toRemove == null)
            return;

        _container.Remove(toRemove.Value, sc.Container);
        if (!_hands.TryPickupAnyHand(user, toRemove.Value))
            _xform.DropNextTo(toRemove.Value, user);
        RefreshMarketState(terminal);
    }

    private void OnProtoSearch(Entity<MarketTerminalComponent> terminal, ref ProtoSearchMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        if (string.IsNullOrWhiteSpace(msg.Query)) return;

        var query = msg.Query.ToLowerInvariant();
        var matches = new List<(string Id, string Name)>();

        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            var id = proto.ID.ToLowerInvariant();
            if (!id.StartsWith("n14") && !id.StartsWith("misfits"))
                continue;

            var hasItem = proto.Components.ContainsKey("Item");
            var hasClothing = proto.Components.ContainsKey("Clothing");
            if (!hasItem && !hasClothing)
                continue;

            var rawName = proto.Name ?? string.Empty;
            var displayName = rawName;
            if (!string.IsNullOrWhiteSpace(rawName) && Loc.TryGetString(rawName, out var localized))
                displayName = localized;

            if (!id.Contains(query) && !rawName.ToLowerInvariant().Contains(query) && !displayName.ToLowerInvariant().Contains(query))
                continue;

            matches.Add((proto.ID, displayName));
            if (matches.Count >= 20)
                break;
        }

        // #Cythisiax Add - Store search results per player instead of globally.
        _searchResultsByUser[actor.PlayerSession.UserId.UserId] = (msg.Query, matches);
        _ui.ServerSendUiMessage(terminal.Owner, MarketUiKey.Key, new ProtoSearchResults(matches), msg.Actor);
        RefreshMarketState(terminal);
    }

    private void OnSelectOrderBook(Entity<MarketTerminalComponent> terminal, ref SelectOrderBookMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        var uid = actor.PlayerSession.UserId.UserId;
        if (string.IsNullOrWhiteSpace(msg.PrototypeId))
            return;

        _selectedProtoByUser[uid] = msg.PrototypeId;
        RefreshMarketState(terminal);
    }

    // ── Order Matching Engine ──────────────────────────────────────────────────

    private void OnCreateOrder(Entity<MarketTerminalComponent> terminal, ref CreateOrderMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        var session = actor.PlayerSession;
        var user = session.AttachedEntity;
        if (user == null || user == null || user == EntityUid.Invalid) return;
        var userId = session.UserId;
        var charName = session.Name;
        var orderId = Guid.NewGuid().ToString();

        if (msg.IsBuyOrder)
        {
            var totalCost = msg.Price * msg.Quantity;
            if (!TryDeductFee(user.Value, msg.Currency, totalCost + totalCost / 10)) return;
            _escrowCurrency[(userId.UserId, orderId)] = totalCost;
        }
        else
        {
            var comp = terminal.Comp;
            if (!comp.PlayerStorage.TryGetValue(userId.UserId, out var storageUid) || !Exists(storageUid)
                || !TryComp<StorageComponent>(storageUid, out var sc)) return;
            EntityUid? item = null;
            EntityUid escrowItem;
            foreach (var c in sc.Container.ContainedEntities)
            {
                if (MetaData(c).EntityPrototype?.ID != msg.PrototypeId)
                    continue;

                item = c;
                break;
            }
            if (item == null) return;
            var escrowContainer = _container.EnsureContainer<ContainerSlot>(terminal.Owner, $"{ListingSlotPrefix}{orderId}");
            if (TryComp(item.Value, out StackComponent? stack) && stack.Count > msg.Quantity)
            {
                var split = _stack.Split(item.Value, msg.Quantity, Transform(storageUid).Coordinates, stack);
                if (split == null)
                    return;

                escrowItem = split.Value;
            }
            else
            {
                if (TryComp(item.Value, out StackComponent? sourceStack) && sourceStack.Count < msg.Quantity)
                    return;

                _container.Remove(item.Value, sc.Container);
                escrowItem = item.Value;
            }

            if (!_container.Insert(escrowItem, escrowContainer))
                return;
            _escrowItems[(userId.UserId, orderId)] = (msg.PrototypeId, msg.Quantity);
        }

        var protoName = _proto.TryIndex<EntityPrototype>(msg.PrototypeId, out var p) ? p.Name : msg.PrototypeId;
        var order = new MarketOrder
        {
            OrderId = orderId, PrototypeId = msg.PrototypeId, PrototypeName = protoName,
            Quantity = msg.Quantity, Price = msg.Price, Currency = msg.Currency,
            IsBuyOrder = msg.IsBuyOrder, OwnerName = charName, OwnerId = userId.UserId,
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(3),
        };

        MatchOrder(order);
        if (order.Status != "Fulfilled") _activeOrders[orderId] = order;
        _selectedProtoByUser[userId.UserId] = msg.PrototypeId;
        if (order.FulfilledQty > 0) PushFeed(msg.IsBuyOrder
            ? $"{charName} bought {order.FulfilledQty}x {order.PrototypeId} @ {order.Price} {order.Currency}"
            : $"{charName} sold {order.FulfilledQty}x {order.PrototypeId} @ {order.Price} {order.Currency}");
        RefreshMarketState(terminal);
    }

    private void MatchOrder(MarketOrder o) { if (o.IsBuyOrder) MatchBuy(o); else MatchSell(o); }

    private void MatchBuy(MarketOrder buy)
    {
        var sells = _activeOrders.Values
            .Where(o => o.Status == "Active" && !o.IsBuyOrder && o.PrototypeId == buy.PrototypeId && o.Price <= buy.Price)
            .OrderBy(o => o.Price).ToList();
        var rem = buy.Quantity;
        foreach (var s in sells)
        {
            if (rem <= 0) break;
            var avail = s.Quantity - s.FulfilledQty;
            if (avail <= 0) continue;
            var fill = Math.Min(rem, avail);
            s.FulfilledQty += fill; buy.FulfilledQty += fill; rem -= fill;
            var proceeds = s.Price * fill;
            _ = CreditSellerAsync(s.OwnerId, s.OwnerName, s.Currency, proceeds - proceeds / 10);
            if (s.FulfilledQty >= s.Quantity) s.Status = "Fulfilled";
        }
        buy.Status = buy.FulfilledQty >= buy.Quantity ? "Fulfilled" : "Active";
    }

    private void MatchSell(MarketOrder sell)
    {
        var buys = _activeOrders.Values
            .Where(o => o.Status == "Active" && o.IsBuyOrder && o.PrototypeId == sell.PrototypeId && o.Price >= sell.Price)
            .OrderByDescending(o => o.Price).ToList();
        var rem = sell.Quantity;
        foreach (var b in buys)
        {
            if (rem <= 0) break;
            var avail = b.Quantity - b.FulfilledQty;
            if (avail <= 0) continue;
            var fill = Math.Min(rem, avail);
            b.FulfilledQty += fill; sell.FulfilledQty += fill; rem -= fill;
            _ = CreditSellerAsync(sell.OwnerId, sell.OwnerName, sell.Currency, b.Price * fill - (b.Price * fill) / 10);
            if (b.FulfilledQty >= b.Quantity) b.Status = "Fulfilled";
        }
        sell.Status = sell.FulfilledQty >= sell.Quantity ? "Fulfilled" : "Active";
    }

    private void OnCancelOrder(Entity<MarketTerminalComponent> terminal, ref CancelOrderMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        var user = actor.PlayerSession.AttachedEntity;
        if (user == null || user == null || user == EntityUid.Invalid) return;
        if (!_activeOrders.TryGetValue(msg.OrderId, out var order) || order.Status != "Active") return;
        if (order.OwnerId != actor.PlayerSession.UserId.UserId) return;
        order.Status = "Cancelled"; _activeOrders.Remove(msg.OrderId);
        if (order.IsBuyOrder)
        {
            if (_escrowCurrency.TryGetValue((order.OwnerId, order.OrderId), out var refund))
            { RefundCurrency(user.Value, order.Currency, refund); _escrowCurrency.Remove((order.OwnerId, order.OrderId)); }
        }
        else
        {
            var sn = $"{ListingSlotPrefix}{msg.OrderId}";
            if (_container.TryGetContainer(terminal.Owner, sn, out var c) && c is ContainerSlot slot && slot.ContainedEntity is { } item)
            { _container.Remove(item, slot); if (!_hands.TryPickupAnyHand(user.Value, item)) _xform.DropNextTo(item, user.Value); }
        }
        RefreshMarketState(terminal);
    }

    private void OnClaimEscrow(Entity<MarketTerminalComponent> terminal, ref ClaimEscrowMessage msg)
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        var user = actor.PlayerSession.AttachedEntity;
        if (user == null || user == null || user == EntityUid.Invalid) return;
        if (!_activeOrders.TryGetValue(msg.OrderId, out var order)) return;
        if (order.OwnerId != actor.PlayerSession.UserId.UserId || order.Status != "Fulfilled") return;
        if (order.IsBuyOrder)
        {
            var sn = $"{ListingSlotPrefix}{msg.OrderId}";
            if (_container.TryGetContainer(terminal.Owner, sn, out var c) && c is ContainerSlot slot && slot.ContainedEntity is { } item)
            { _container.Remove(item, slot); if (!_hands.TryPickupAnyHand(user.Value, item)) _xform.DropNextTo(item, user.Value); }
            else { var s = Spawn(order.PrototypeId, Transform(user.Value).Coordinates); if (!_hands.TryPickupAnyHand(user.Value, s)) _xform.DropNextTo(s, user.Value); }
        }
        else
        {
            if (_escrowCurrency.TryGetValue((order.OwnerId, order.OrderId), out var proceeds))
            { RefundCurrency(user.Value, order.Currency, proceeds); _escrowCurrency.Remove((order.OwnerId, order.OrderId)); }
        }
        order.Status = "Claimed"; RefreshMarketState(terminal);
    }

    // ── Currency Helpers ──────────────────────────────────────────────────────

    private bool TryDeductFee(EntityUid user, string currency, int amount)
    {
        if (amount <= 0) return true;
        var ct = currency switch { "Bottlecaps" => CurrencyType.Bottlecaps, "NCRDollars" => CurrencyType.NCRDollars, _ => (CurrencyType?)null };
        if (ct == null || !TryComp<PersistentCurrencyComponent>(user, out var w)) return false;
        var bal = ct switch { CurrencyType.Bottlecaps => w.Bottlecaps, CurrencyType.NCRDollars => w.NcrDollars, _ => 0 };
        if (bal < amount) return false;
        switch (ct) { case CurrencyType.Bottlecaps: w.Bottlecaps -= amount; break; case CurrencyType.NCRDollars: w.NcrDollars -= amount; break; }
        Dirty(user, w);
        if (w.UserId != null && w.CharacterName != null && Guid.TryParse(w.UserId, out var pid))
            _ = _db.UpsertCharacterCurrencyAsync(pid, w.CharacterName, w.Bottlecaps, w.NcrDollars, w.Silver, w.Gold);
        return true;
    }

    private void RefundCurrency(EntityUid user, string currency, int amount)
    {
        if (amount <= 0 || !TryComp<PersistentCurrencyComponent>(user, out var w)) return;
        switch (currency) { case "Bottlecaps": w.Bottlecaps += amount; break; case "NCRDollars": w.NcrDollars += amount; break; }
        Dirty(user, w);
        if (w.UserId != null && w.CharacterName != null && Guid.TryParse(w.UserId, out var pid))
            _ = _db.UpsertCharacterCurrencyAsync(pid, w.CharacterName, w.Bottlecaps, w.NcrDollars, w.Silver, w.Gold);
    }

    private async Task CreditSellerAsync(Guid sellerId, string name, string currency, int amount)
    {
        if (amount <= 0) return;
        try
        {
            var row = await _db.GetCharacterCurrencyAsync(sellerId, name);
            var caps = (row?.Bottlecaps ?? 0) + (currency == "Bottlecaps" ? amount : 0);
            var ncr = (row?.NcrDollars ?? 0) + (currency == "NCRDollars" ? amount : 0);
            var sil = row?.Silver ?? 0;
            var gld = row?.Gold ?? 0;
            await _db.UpsertCharacterCurrencyAsync(sellerId, name, caps, ncr, sil, gld);
        }
        catch (Exception ex) { _log.Error($"CreditSellerAsync failed: {ex}"); }
    }

    // ── Feed & State ──────────────────────────────────────────────────────────

    private void PushFeed(string text)
    {
        _activityFeed.Insert(0, new MarketFeedEntry { Text = text, Time = DateTime.UtcNow });
        if (_activityFeed.Count > MaxFeedEntries) _activityFeed.RemoveAt(_activityFeed.Count - 1);
    }

    private void RefreshMarketState(Entity<MarketTerminalComponent> terminal)
    {
        foreach (var user in _openMarketUis.ToList())
        {
            if (!_ui.IsUiOpen(terminal.Owner, MarketUiKey.Key, user)) continue;
            _ui.SetUiState(terminal.Owner, MarketUiKey.Key, BuildState(terminal, user));
        }
    }

    private MarketStateMessage BuildState(Entity<MarketTerminalComponent> terminal, EntityUid user)
    {
        var state = new MarketStateMessage { Feed = new List<MarketFeedEntry>(_activityFeed) };
        if (TryComp<PersistentCurrencyComponent>(user, out var w))
        { state.Bottlecaps = w.Bottlecaps; state.NcrDollars = w.NcrDollars; }

        var activeOrders = _activeOrders.Values.Where(o => o.Status == "Active").ToList();
        state.ItemSummaries = BuildItemSummaries(activeOrders);

        if (TryComp<ActorComponent>(user, out var actor))
        {
            var uid = actor.PlayerSession.UserId.UserId;
            state.MyOrders = _activeOrders.Values.Where(o => o.OwnerId == uid && o.Status == "Active").ToList();
            state.MyCompletedOrders = _activeOrders.Values.Where(o => o.OwnerId == uid && o.Status == "Fulfilled").ToList();
            if (_searchResultsByUser.TryGetValue(uid, out var search))
            {
                state.LastSearchQuery = search.Query;
                state.SearchResults = new List<(string, string)>(search.Results);
            }

            var selectedProtoId = GetSelectedPrototypeId(uid, activeOrders);
            if (!string.IsNullOrWhiteSpace(selectedProtoId))
            {
                state.SelectedProtoId = selectedProtoId;
                state.SelectedProtoName = GetPrototypeName(selectedProtoId, state.ItemSummaries, activeOrders);
                state.SelectedOrderBook = BuildOrderBook(selectedProtoId, state.SelectedProtoName, activeOrders);
            }

            var comp = terminal.Comp;
            if (comp.PlayerStorage.TryGetValue(uid, out var storage) && Exists(storage)
                && TryComp<StorageComponent>(storage, out var sc) && sc.Container != null)
            {
                foreach (var item in sc.Container.ContainedEntities)
                {
                    var meta = MetaData(item);
                    state.DepositedItems.Add(new MarketDepositEntry
                    {
                        SlotKey = item.ToString(),
                        // #Cythisiax Add - Slot key is the exact entity id so withdraw
                        // can target the right entry inside multi-item market storage.
                        ProtoId = meta.EntityPrototype?.ID ?? "",
                        ProtoName = meta.EntityPrototype?.Name ?? meta.EntityName,
                        StackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : 0,
                    });
                }
            }
        }
        state.MarketName = "Wendover Free Market Exchange";
        return state;
    }

    private string GetSelectedPrototypeId(Guid userId, List<MarketOrder> activeOrders)
    {
        if (_selectedProtoByUser.TryGetValue(userId, out var selected) && !string.IsNullOrWhiteSpace(selected))
            return selected;

        var first = activeOrders.FirstOrDefault();
        if (first == null)
            return string.Empty;

        _selectedProtoByUser[userId] = first.PrototypeId;
        return first.PrototypeId;
    }

    private static string GetPrototypeName(string prototypeId, List<MarketItemSummary> summaries, List<MarketOrder> activeOrders)
    {
        var summary = summaries.FirstOrDefault(s => s.PrototypeId == prototypeId);
        if (!string.IsNullOrWhiteSpace(summary?.PrototypeName))
            return summary.PrototypeName;

        var order = activeOrders.FirstOrDefault(o => o.PrototypeId == prototypeId);
        return !string.IsNullOrWhiteSpace(order?.PrototypeName) ? order.PrototypeName : prototypeId;
    }

    private List<MarketItemSummary> BuildItemSummaries(List<MarketOrder> activeOrders)
    {
        var summaries = new List<MarketItemSummary>();
        foreach (var group in activeOrders.GroupBy(o => o.PrototypeId))
        {
            var orders = group.ToList();
            var prototypeName = orders.FirstOrDefault()?.PrototypeName ?? group.Key;
            var sellOrders = orders.Where(o => !o.IsBuyOrder).ToList();
            var buyOrders = orders.Where(o => o.IsBuyOrder).ToList();
            var bestAsk = sellOrders.Count > 0 ? sellOrders.Min(o => o.Price) : 0;
            var bestBid = buyOrders.Count > 0 ? buyOrders.Max(o => o.Price) : 0;
            var currencies = orders.Select(o => o.Currency).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

            summaries.Add(new MarketItemSummary
            {
                PrototypeId = group.Key,
                PrototypeName = prototypeName,
                OrderCount = orders.Count,
                BestAsk = bestAsk,
                BestBid = bestBid,
                Spread = bestAsk > 0 && bestBid > 0 ? bestAsk - bestBid : 0,
                Currency = currencies.Count == 1 ? currencies[0] : string.Join("/", currencies),
            });
        }

        return summaries
            .OrderBy(s => s.PrototypeName)
            .ThenBy(s => s.PrototypeId)
            .ToList();
    }

    private static OrderBookEntry? BuildOrderBook(string prototypeId, string prototypeName, List<MarketOrder> activeOrders)
    {
        var orders = activeOrders.Where(o => o.PrototypeId == prototypeId).ToList();
        if (orders.Count == 0)
            return null;

        return new OrderBookEntry
        {
            PrototypeId = prototypeId,
            PrototypeName = prototypeName,
            SellOrders = orders.Where(o => !o.IsBuyOrder).OrderBy(o => o.Price).ThenBy(o => o.CreatedAt).ToList(),
            BuyOrders = orders.Where(o => o.IsBuyOrder).OrderByDescending(o => o.Price).ThenBy(o => o.CreatedAt).ToList(),
            Volume24h = orders.Sum(o => o.FulfilledQty),
        };
    }

    // ── Round lifecycle ───────────────────────────────────────────────────────

    private void OnRoundStarted(RoundStartedEvent args)
    {
        _activeOrders.Clear(); _openMarketUis.Clear(); _activityFeed.Clear();
        _escrowCurrency.Clear(); _escrowItems.Clear();
        _searchResultsByUser.Clear();
    }
}
