#nullable enable
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using BTCPayServer.Services.Apps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.MakePay.EventTickets;

public sealed class EventTicketsPlugin : BaseBTCPayServerPlugin
{
    public const string PluginVersion = "1.6.0";
    public const string SettingsKey = "MakePay.EventTickets.Settings";
    public const string EventsKey = "MakePay.EventTickets.Events";
    public const string OrdersKey = "MakePay.EventTickets.Orders";
    public const string TicketsKey = "MakePay.EventTickets.Tickets";
    public override string Identifier => "BTCPayServer.Plugins.MakePay.EventTickets";
    public override string Name => "MakePay Event Tickets";
    public override string Description => "Sell event tickets, deliver wallet passes and PDFs, and manage QR admissions.";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } = [new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.5" }];
    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<EventTicketRepository>();
        services.AddSingleton<EventTicketsAppService>();
        services.AddSingleton<AppBaseType, EventTicketsAppType>();
        services.AddSingleton<TicketCodeService>();
        services.AddSingleton<TicketCheckoutService>();
        services.AddSingleton<TicketDocumentService>();
        services.AddSingleton<WalletPassService>();
        services.AddHttpClient<TicketEmailService>();
        services.AddSingleton<EventTicketFulfillmentService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<EventTicketFulfillmentService>());
        services.AddUIExtension("store-integrations-nav", "EventTickets/StoreNavExtension");
        base.Execute(services);
    }
}
