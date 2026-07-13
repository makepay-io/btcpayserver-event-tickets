#nullable enable
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class EventTicketFulfillmentService(EventAggregator events, ILogger<EventTicketFulfillmentService> logger, EventTicketRepository repository, TicketCodeService codes, TicketCheckoutService checkout, TicketEmailService email) : EventHostedServiceBase(events, logger)
{
    public const string TagPrefix = "MPET#";
    public static string Tag(string orderId) => TagPrefix + orderId;
    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent) return;
        var orderId = invoiceEvent.Invoice.GetInternalTags(TagPrefix).FirstOrDefault(); if (orderId is null) return;
        if (invoiceEvent.EventCode == InvoiceEventCode.Expired)
        {
            if (!invoiceEvent.PaidPartial && !invoiceEvent.Invoice.GetPayments(false).Any()) await repository.CancelOrder(invoiceEvent.Invoice.StoreId, orderId);
            return;
        }
        if (invoiceEvent.EventCode == InvoiceEventCode.ExpiredPaidPartial) return;
        if (invoiceEvent.EventCode is InvoiceEventCode.MarkedInvalid or InvoiceEventCode.FailedToConfirm) { await repository.CancelOrder(invoiceEvent.Invoice.StoreId, orderId); return; }
        var settings = await repository.GetSettings(invoiceEvent.Invoice.StoreId);
        var eligible = invoiceEvent.EventCode is InvoiceEventCode.Completed or InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted || settings.DeliverOnProcessing && invoiceEvent.EventCode == InvoiceEventCode.PaidInFull; if (!eligible) return;
        var order = await repository.GetOrder(invoiceEvent.Invoice.StoreId, orderId); if (order is null || order.Status == TicketOrderStatus.Paid) return;
        var item = await repository.GetEvent(order.StoreId, order.EventId); if (item is null) return;
        var lines = TicketCheckoutService.ResolveLines(order, item); if (lines.Count == 0) return;
        var tickets = new List<IssuedTicket>();
        var attendeeQueues = order.Attendees.GroupBy(attendee => attendee.TicketTypeId).ToDictionary(group => group.Key, group => new Queue<TicketAttendee>(group), StringComparer.OrdinalIgnoreCase);
        var sequence = 0;
        foreach (var line in lines)
        {
            if (item.TicketTypes.All(type => type.Id != line.TicketTypeId)) continue;
            for (var index = 0; index < line.Quantity; index++)
            {
                sequence++;
                attendeeQueues.TryGetValue(line.TicketTypeId, out var queue);
                var attendee = queue is { Count: > 0 } ? queue.Dequeue() : null;
                var created = codes.Create();
                tickets.Add(new IssuedTicket
                {
                    StoreId = order.StoreId,
                    EventId = item.Id,
                    TicketTypeId = line.TicketTypeId,
                    OrderId = order.Id,
                    AttendeeName = attendee is null ? (order.Quantity == 1 ? order.BuyerName : $"{order.BuyerName} #{sequence}") : (attendee.FirstName + " " + attendee.LastName).Trim(),
                    AttendeeEmail = attendee?.Email ?? order.BuyerEmail,
                    CodeHash = created.Hash,
                    ProtectedCode = created.Protected
                });
            }
        }
        await repository.SaveTickets(order.StoreId, tickets); order.TicketIds = tickets.Select(t => t.Id).ToList(); order.Status = TicketOrderStatus.Paid; order.PaidAt = DateTimeOffset.UtcNow; await repository.SaveOrder(order.StoreId, order);
        var accessToken = checkout.GetAccessToken(order);
        var orderUrl = order.PublicBaseUrl.TrimEnd('/') + $"/stores/{order.StoreId}/events/order/{order.Id}" + (accessToken is null ? "" : $"?accessToken={Uri.EscapeDataString(accessToken)}");
        try { await email.Send(order.StoreId, settings, item, order, tickets, orderUrl, cancellationToken); order.DeliverySent = true; await repository.SaveOrder(order.StoreId, order); }
        catch (Exception ex) { logger.LogWarning(ex, "Ticket delivery failed for order {OrderId}; tickets remain available on the order page.", order.Id); }
    }
}
