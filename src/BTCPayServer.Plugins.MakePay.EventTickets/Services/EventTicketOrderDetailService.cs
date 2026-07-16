#nullable enable
using BTCPayServer.Plugins.MakePay.EventTickets.Models;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public static class EventTicketOrderDetailService
{
    private sealed record ResolvedLine(string TicketTypeId, int Quantity, decimal? UnitPrice);

    public static EventTicketOrderDetailViewModel Build(
        string storeId,
        TicketOrder order,
        TicketEvent? item,
        IEnumerable<IssuedTicket> issuedTickets,
        EventTicketOrderQuery? returnQuery = null,
        EventTicketInvoiceSnapshot? invoice = null)
    {
        var ticketTypes = item?.TicketTypes.ToDictionary(type => type.Id, StringComparer.OrdinalIgnoreCase)
                          ?? new Dictionary<string, TicketType>(StringComparer.OrdinalIgnoreCase);
        var lines = ResolveLines(order, invoice)
            .Select(line => new EventTicketOrderLineDetail
            {
                TicketTypeId = line.TicketTypeId,
                TicketTypeName = TicketTypeName(ticketTypes, line.TicketTypeId),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            })
            .ToList();
        var attendees = (order.Attendees ?? [])
            .Select(attendee => new EventTicketAttendeeDetail
            {
                TicketTypeId = attendee.TicketTypeId,
                TicketTypeName = TicketTypeName(ticketTypes, attendee.TicketTypeId),
                FirstName = attendee.FirstName,
                LastName = attendee.LastName,
                Nickname = attendee.Nickname,
                Email = attendee.Email,
                Phone = attendee.Phone,
                Country = attendee.Country,
                Company = attendee.Company
            })
            .ToList();
        var orderTicketIds = new HashSet<string>(order.TicketIds ?? [], StringComparer.OrdinalIgnoreCase);
        var tickets = issuedTickets
            .Where(ticket => string.Equals(ticket.OrderId, order.Id, StringComparison.OrdinalIgnoreCase) ||
                             orderTicketIds.Contains(ticket.Id))
            .OrderBy(ticket => ticket.IssuedAt)
            .ThenBy(ticket => ticket.Id, StringComparer.OrdinalIgnoreCase)
            .Select(ticket => Project(ticket, ticketTypes))
            .ToList();

        return new EventTicketOrderDetailViewModel
        {
            StoreId = storeId,
            Order = Project(order, invoice),
            Event = item is null ? null : Project(item),
            Lines = lines,
            Attendees = attendees,
            Tickets = tickets,
            ReturnQuery = returnQuery ?? new EventTicketOrderQuery()
        };
    }

    private static EventTicketAdminOrderDetail Project(TicketOrder order, EventTicketInvoiceSnapshot? invoice)
    {
        // Orders created before the multi-step checkout did not persist monetary or
        // reservation snapshots. Their property initializers are populated during
        // deserialization, so zero USD and ReservationExpiresAt cannot be presented
        // as historical facts. Prefer a tenant-checked invoice snapshot for money;
        // otherwise leave the values explicitly unavailable.
        var hasStoredAmounts = order.Lines is { Count: > 0 } ||
                               order.Subtotal != 0m || order.Total != 0m || order.DiscountAmount != 0m;
        var invoiceAmounts = !hasStoredAmounts && invoice is not null;
        return new EventTicketAdminOrderDetail
        {
            Id = order.Id,
            EventId = order.EventId,
            BuyerEmail = order.BuyerEmail,
            BuyerName = order.BuyerName,
            BuyerFirstName = order.BuyerFirstName,
            BuyerLastName = order.BuyerLastName,
            BuyerPhone = order.BuyerPhone,
            BuyerCountry = order.BuyerCountry,
            BuyerCompany = order.BuyerCompany,
            Currency = hasStoredAmounts ? order.Currency : invoice?.Currency,
            Subtotal = hasStoredAmounts ? order.Subtotal : null,
            DiscountAmount = hasStoredAmounts ? order.DiscountAmount : null,
            Total = hasStoredAmounts ? order.Total : invoice?.Amount,
            AmountsFromInvoice = invoiceAmounts,
            PromoCode = order.PromoCode,
            InvoiceId = order.InvoiceId,
            PosMode = order.PosMode,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            ReservationExpiresAt = order.Lines is { Count: > 0 } ? order.ReservationExpiresAt : null,
            TermsAcceptedAt = order.TermsAcceptedAt,
            PaidAt = order.PaidAt,
            DeliverySent = order.DeliverySent
        };
    }

    private static EventTicketAdminEventDetail Project(TicketEvent item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Slug = item.Slug,
        VenueName = item.VenueName,
        VenueAddress = item.VenueAddress,
        StartsAt = item.StartsAt,
        EndsAt = item.EndsAt,
        RequireIdCheck = item.RequireIdCheck
    };

    private static IReadOnlyList<ResolvedLine> ResolveLines(
        TicketOrder order,
        EventTicketInvoiceSnapshot? invoice)
    {
        if (order.Lines is { Count: > 0 })
            return order.Lines
                .Select(line => new ResolvedLine(line.TicketTypeId, line.Quantity, line.UnitPrice))
                .ToList();
        if (string.IsNullOrWhiteSpace(order.TicketTypeId) || order.Quantity < 1) return [];

        decimal? storedSubtotal = order.Subtotal != 0m
            ? order.Subtotal
            : order.Total != 0m || order.DiscountAmount != 0m
                ? order.Total + order.DiscountAmount
                : invoice?.Amount;
        var unitPrice = storedSubtotal / order.Quantity;
        return [new ResolvedLine(order.TicketTypeId, order.Quantity, unitPrice)];
    }

    private static EventTicketIssuedTicketDetail Project(
        IssuedTicket ticket,
        IReadOnlyDictionary<string, TicketType> ticketTypes)
    {
        var isInside = EventTicketRepository.IsCurrentlyInside(ticket);
        var lastActivity = ticket.IssuedAt;
        if (ticket.CheckedInAt is { } checkedInAt && checkedInAt > lastActivity) lastActivity = checkedInAt;
        if (ticket.CheckedOutAt is { } checkedOutAt && checkedOutAt > lastActivity) lastActivity = checkedOutAt;
        if (ticket.LastIdCheckedAt is { } idCheckedAt && idCheckedAt > lastActivity) lastActivity = idCheckedAt;

        return new EventTicketIssuedTicketDetail
        {
            TicketId = ticket.Id,
            TicketTypeId = ticket.TicketTypeId,
            TicketTypeName = TicketTypeName(ticketTypes, ticket.TicketTypeId),
            AttendeeName = ticket.AttendeeName,
            AttendeeEmail = ticket.AttendeeEmail,
            IssuedAt = ticket.IssuedAt,
            CheckedInAt = ticket.CheckedInAt,
            CheckedInBy = ticket.CheckedInBy,
            CheckInGate = ticket.CheckInGate,
            CheckedOutAt = ticket.CheckedOutAt,
            CheckedOutBy = ticket.CheckedOutBy,
            CheckOutGate = ticket.CheckOutGate,
            EntranceCount = EventTicketRepository.EffectiveEntranceCount(ticket),
            IdConfirmedCount = ticket.IdConfirmedCount,
            IdRejectedCount = ticket.IdRejectedCount,
            LastIdCheckedAt = ticket.LastIdCheckedAt,
            LastIdCheckedBy = ticket.LastIdCheckedBy,
            LastIdCheckConfirmed = ticket.LastIdCheckConfirmed,
            Revoked = ticket.Revoked,
            IsInside = isInside,
            LastActivityAt = lastActivity,
            LastGate = isInside ? ticket.CheckInGate : ticket.CheckOutGate
        };
    }

    private static string TicketTypeName(
        IReadOnlyDictionary<string, TicketType> ticketTypes,
        string ticketTypeId) =>
        ticketTypes.TryGetValue(ticketTypeId, out var ticketType) && !string.IsNullOrWhiteSpace(ticketType.Name)
            ? ticketType.Name
            : "Deleted ticket type";
}
