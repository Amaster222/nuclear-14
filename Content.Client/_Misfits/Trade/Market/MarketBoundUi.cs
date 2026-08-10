// #Cythisiax Add - Market terminal client BUI
using Content.Shared._Misfits.Trade.Market;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Trade.Market;

public sealed class MarketBoundUi(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private MarketWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new MarketWindow();
        _window.OnClose += Close;
        _window.OnListRequest += msg => SendMessage(msg);
        _window.OnBuyRequest += (id, _) => SendMessage(new SelectOrderBookMessage(id));
        _window.OnClaim += orderId => SendMessage(new ClaimEscrowMessage(orderId));
        _window.OnCancel += orderId => SendMessage(new CancelOrderMessage(orderId));
        _window.OnProtoSearch += query => SendMessage(new ProtoSearchMessage(query));
        _window.OnBarterSearch += query => SendMessage(new ProtoSearchMessage(query));

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not MarketStateMessage marketState)
            return;

        // #Cythisiax Add - Keep the title in sync with the server state.
        _window?.SetMarketName(marketState.MarketName);
        _window?.UpdateItemSummaries(marketState.ItemSummaries);
        _window?.UpdateOrderBook(marketState.SelectedOrderBook);
        _window?.UpdateMyOrders(marketState.MyOrders, marketState.MyCompletedOrders);
        _window?.UpdateFeed(marketState.Feed);
        _window?.UpdateDepositedItems(marketState.DepositedItems,
            slotKey => SendMessage(new MarketWithdrawItemMessage(slotKey)));
        _window?.UpdateBalances(marketState.Bottlecaps, marketState.NcrDollars);
        _window?.OnSearchResults(marketState.SearchResults);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is not ProtoSearchResults results)
            return;

        _window?.OnSearchResults(results.Results);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_window != null)
            _window.OnClose -= Close;
        _window?.Close();
        _window?.Dispose();
    }
}
