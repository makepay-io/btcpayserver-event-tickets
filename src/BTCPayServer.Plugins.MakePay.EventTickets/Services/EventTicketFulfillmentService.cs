#nullable enable
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class EventTicketFulfillmentService(EventAggregator events, ILogger<EventTicketFulfillmentService> logger, EventTicketRepository repository, TicketCodeService codes, TicketEmailService email) : EventHostedServiceBase(events, logger)
{
    public const string TagPrefix = "MPET#";
    public static string Tag(string orderId) => TagPrefix + orderId;
    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent) return;
        var orderId = invoiceEvent.Invoice.GetInternalTags(TagPrefix).FirstOrDefault(); if (orderId is null) return;
        if (invoiceEvent.EventCode is InvoiceEventCode.Expired or InvoiceEventCode.MarkedInvalid or InvoiceEventCode.FailedToConfirm) { await repository.CancelOrder(invoiceEvent.Invoice.StoreId, orderId); return; }
        var settings = await repository.GetSettings(invoiceEvent.Invoice.StoreId);
        var eligible = invoiceEvent.EventCode is InvoiceEventCode.Completed or InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted || settings.DeliverOnProcessing && invoiceEvent.EventCode == InvoiceEventCode.PaidInFull; if (!eligible) return;
        var order = await repository.GetOrder(invoiceEvent.Invoice.StoreId, orderId); if (order is null || order.Status == TicketOrderStatus.Paid) return;
        var item = await repository.GetEvent(order.StoreId, order.EventId); var type = item?.TicketTypes.FirstOrDefault(t => t.Id == order.TicketTypeId); if (item is null || type is null) return;
        var tickets = new List<IssuedTicket>();
        for (var i = 0; i < order.Quantity; i++) { var created = codes.Create(); tickets.Add(new() { StoreId = order.StoreId, EventId = item.Id, TicketTypeId = type.Id, OrderId = order.Id, AttendeeName = order.Quantity == 1 ? order.BuyerName : $"{order.BuyerName} #{i + 1}", AttendeeEmail = order.BuyerEmail, CodeHash = created.Hash, ProtectedCode = created.Protected }); }
        await repository.SaveTickets(order.StoreId, tickets); order.TicketIds = tickets.Select(t => t.Id).ToList(); order.Status = TicketOrderStatus.Paid; order.PaidAt = DateTimeOffset.UtcNow; await repository.SaveOrder(order.StoreId, order);
        var orderUrl = order.PublicBaseUrl.TrimEnd('/') + $"/stores/{order.StoreId}/events/order/{order.Id}";
        try { await email.Send(order.StoreId, settings, item, type, order, tickets, orderUrl, cancellationToken); order.DeliverySent = true; await repository.SaveOrder(order.StoreId, order); }
        catch (Exception ex) { logger.LogWarning(ex, "Ticket delivery failed for order {OrderId}; tickets remain available on the order page.", order.Id); }
    }
}
