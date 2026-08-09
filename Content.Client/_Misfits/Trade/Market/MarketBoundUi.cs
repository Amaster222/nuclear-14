// #Cythisiax Add - Market terminal client BUI
using Content.Shared._Misfits.Trade.Market;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Trade.Market;

public sealed class MarketBoundUi(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private readonly IPlayerManager _player = IoCManager.Resolve<IPlayerManager>();

    private MarketWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new MarketWindow();
        _window.OnClose += Close;
        _window.OnListRequest += msg => SendMessage(msg);
        _window.OnBuyRequest += (id, _) => { /* item detail select — future */ };
        _window.OnClaim += orderId => SendMessage(new ClaimEscrowMessage(orderId));
        _window.OnCancel += orderId => SendMessage(new CancelOrderMessage(orderId));

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not MarketStateMessage marketState)
            return;

        _window?.SetMarketName(marketState.MarketName);
        _window?.UpdateItemSummaries(marketState.ItemSummaries);
        _window?.UpdateMyOrders(marketState.MyOrders, marketState.MyCompletedOrders);
        _window?.UpdateFeed(marketState.Feed);
        _window?.UpdateDepositedItems(marketState.DepositedItems,
            slotKey => SendMessage(new MarketWithdrawItemMessage(slotKey)));
        _window?.UpdateBalances(marketState.Bottlecaps, marketState.NcrDollars,
            marketState.Silver, marketState.Gold);
    }

    private void OnListRequest(CreateOrderMessage msg) => SendMessage(msg);

    private void OnBuyRequest(string listingId, int quantity) =>
        SendMessage(new CreateOrderMessage(listingId, quantity, "", 0, true));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_window != null)
            _window.OnClose -= Close;
        _window?.Close();
        _window?.Dispose();
    }
}
