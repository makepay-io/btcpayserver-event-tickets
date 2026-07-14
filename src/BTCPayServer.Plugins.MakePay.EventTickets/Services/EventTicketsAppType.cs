#nullable enable
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MakePay.EventTickets.Controllers;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class EventTicketsAppType : AppBaseType
{
    public const string AppType = "MakePayEventTickets";
    private readonly LinkGenerator _links;
    private readonly IOptions<BTCPayServerOptions> _options;

    public EventTicketsAppType(LinkGenerator links, IOptions<BTCPayServerOptions> options) : base(AppType)
    {
        _links = links;
        _options = options;
        Description = "MakePay Event Tickets storefront, checkout, delivery, wallet passes, and QR check-in";
    }

    public override Task<object?> GetInfo(AppData appData) => Task.FromResult<object?>(null);

    public override Task<string> ConfigureLink(AppData app) => Task.FromResult(
        _links.GetPathByAction(nameof(EventTicketsAdminController.Settings), "EventTicketsAdmin",
            new { storeId = app.StoreDataId }, _options.Value.RootPath) ??
        $"{TicketPublicUrl.NormalizeRootPath(_options.Value.RootPath)}/plugins/{app.StoreDataId}/event-tickets/settings");

    // Keep the BTCPay app's regular link on the stable legacy route. Native
    // Policies mapping supplies the optional clean hostname at request time.
    public override Task<string> ViewLink(AppData app) => Task.FromResult(
        _links.GetPathByAction(nameof(EventTicketsPublicControllerBase.Storefront), TicketPublicUrl.LegacyController,
            new { storeId = app.StoreDataId }, _options.Value.RootPath) ??
        $"{TicketPublicUrl.NormalizeRootPath(_options.Value.RootPath)}{TicketPublicUrl.PublicPath(false, app.StoreDataId)}");

    public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
    {
        appData.SetSettings(new EventTicketsAppConfig());
        return Task.CompletedTask;
    }

    public sealed class EventTicketsAppConfig
    {
        public int SchemaVersion { get; set; } = 1;
    }
}
