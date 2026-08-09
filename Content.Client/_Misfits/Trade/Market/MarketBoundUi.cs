// #Cythisiax Add - Market terminal client BUI
using Content.Shared._Misfits.Trade.Market;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Trade.Market;

/// <summary>
/// Client-side bound user interface for the Wendover Free Market terminal.
/// Opens a MarketWindow with Market / My Listings / Activity tabs.
/// </summary>
public sealed class MarketBoundUi(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private readonly IPlayerManager _player = IoCManager.Resolve<IPlayerManager>();

    private MarketWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new MarketWindow();
        _window.OnClose += Close;
        _window.OnListRequest += OnListRequest;
        _window.OnBuyRequest += OnBuyRequest;

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not MarketStateMessage marketState)
            return;

        _window?.SetMarketName(marketState.MarketName);
        _window?.UpdateListings(marketState.Listings);
        _window?.UpdateMyListings(marketState.MyListings);
        _window?.UpdateBalances(marketState.Bottlecaps, marketState.NcrDollars,
            marketState.Silver, marketState.Gold);
    }

    private void OnListRequest(MarketListMessage msg)
    {
        SendMessage(msg);
    }

    private void OnBuyRequest(string listingId, int quantity)
    {
        SendMessage(new MarketBuyMessage(listingId, quantity));
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
