#nullable enable
using System.Collections.Concurrent;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class EventTicketRepository(StoreRepository stores, TicketCodeService codes)
{
    public const string EnforcedPromotionText = "Created by MakePay.io — accept 90+ currencies in a decentralized way with BTCPay Server.";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    public async Task<EventTicketSettings> GetSettings(string storeId)
    {
        var settings = await stores.GetSettingAsync<EventTicketSettings>(storeId, EventTicketsPlugin.SettingsKey) ?? new();
        EnforceMakePayAttribution(settings);
        return settings;
    }

    public Task SaveSettings(string storeId, EventTicketSettings value)
    {
        EnforceMakePayAttribution(value);
        return stores.UpdateSetting(storeId, EventTicketsPlugin.SettingsKey, value);
    }

    public static void EnforceMakePayAttribution(EventTicketSettings settings)
    {
        settings.ShowMakePayPromotion = true;
        settings.PromotionText = EnforcedPromotionText;
    }
    public async Task<IReadOnlyList<TicketEvent>> GetEvents(string storeId) => (await stores.GetSettingAsync<TicketEventCollection>(storeId, EventTicketsPlugin.EventsKey) ?? new()).Events.OrderBy(e => e.StartsAt).ToList();
    public async Task<IReadOnlyList<TicketEvent>> GetEventsWithScannerAccess(string storeId)
    {
        var events = await GetEvents(storeId);
        if (events.All(item => codes.GetScannerAccessToken(item) is not null)) return events;
        await Mutate<TicketEventCollection>(storeId, EventTicketsPlugin.EventsKey, value =>
        {
            foreach (var item in value.Events) codes.EnsureScannerAccessToken(item);
        });
        return await GetEvents(storeId);
    }
    public async Task<TicketEvent?> GetEvent(string storeId, string idOrSlug) => (await GetEvents(storeId)).FirstOrDefault(e => e.Id.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase) || e.Slug.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase));
    public async Task SaveEvent(string storeId, TicketEvent item) => await Mutate<TicketEventCollection>(storeId, EventTicketsPlugin.EventsKey, value =>
    {
        if (value.Events.Any(e => e.Id != item.Id && e.Slug.Equals(item.Slug, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Event slug already exists.");
        codes.EnsureScannerAccessToken(item);
        var index = value.Events.FindIndex(e => e.Id == item.Id); item.UpdatedAt = DateTimeOffset.UtcNow; if (index < 0) value.Events.Add(item); else value.Events[index] = item;
    });
    public async Task<bool> RotateScannerAccess(string storeId, string eventId)
    {
        var rotated = false;
        await MutateIfChanged<TicketEventCollection>(storeId, EventTicketsPlugin.EventsKey, value =>
        {
            var item = value.Events.FirstOrDefault(candidate => candidate.Id.Equals(eventId, StringComparison.OrdinalIgnoreCase));
            if (item is null) return false;
            codes.RotateScannerAccessToken(item);
            item.UpdatedAt = DateTimeOffset.UtcNow;
            return rotated = true;
        });
        return rotated;
    }
    public async Task<IReadOnlyList<TicketOrder>> GetOrders(string storeId) => (await stores.GetSettingAsync<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey) ?? new()).Orders.Values.OrderByDescending(o => o.CreatedAt).ToList();
    public async Task<TicketOrder?> GetOrder(string storeId, string id) => (await stores.GetSettingAsync<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey) ?? new()).Orders.GetValueOrDefault(id);
    public async Task<TicketOrder?> TryCreateOrder(string storeId, TicketEvent item, EventTicketSettings settings, IReadOnlyList<TicketOrderLine> lines)
    {
        TicketOrder? result = null;
        await Mutate<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey, orders =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var stale in orders.Orders.Values.Where(order => TicketReservationPolicy.CanExpire(order, now))) stale.Status = TicketOrderStatus.Cancelled;
            if (!TicketEventSalePolicy.CanStartCheckout(item, now) || lines.Count == 0) return;
            foreach (var line in lines)
            {
                var type = item.TicketTypes.FirstOrDefault(ticketType => ticketType.Id == line.TicketTypeId && ticketType.Active);
                if (type is null || line.Quantity < 1 || line.Quantity > type.MaxPerOrder) return;
                var reserved = orders.Orders.Values
                    .Where(order => order.EventId == item.Id && TicketReservationPolicy.HoldsInventory(order, now))
                    .SelectMany(order => TicketCheckoutService.ResolveLines(order, item))
                    .Where(existing => existing.TicketTypeId == type.Id)
                    .Sum(existing => existing.Quantity);
                if (type.Capacity > 0 && reserved + line.Quantity > type.Capacity) return;
            }

            result = new()
            {
                StoreId = storeId,
                EventId = item.Id,
                Lines = lines.Select(line => new TicketOrderLine { TicketTypeId = line.TicketTypeId, Quantity = line.Quantity, UnitPrice = line.UnitPrice }).ToList(),
                Currency = settings.Currency.ToUpperInvariant(),
                ReservationExpiresAt = now.AddMinutes(settings.CheckoutMinutes)
            };
            TicketCheckoutService.Recalculate(result);
            orders.Orders[result.Id] = result;
        });
        return result;
    }

    public async Task<TicketOrder?> TryCreateOrder(string storeId, TicketEvent item, TicketType type, int quantity, string email, string buyerName)
    {
        var settings = await GetSettings(storeId);
        var order = await TryCreateOrder(storeId, item, settings, [new TicketOrderLine { TicketTypeId = type.Id, Quantity = quantity, UnitPrice = type.Price }]);
        if (order is null) return null;
        order.BuyerEmail = email;
        order.BuyerName = buyerName;
        await SaveOrder(storeId, order);
        return order;
    }
    public async Task SaveOrder(string storeId, TicketOrder order) => await Mutate<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey, value => value.Orders[order.Id] = order);
    public async Task CancelOrder(string storeId, string orderId) => await Mutate<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey, value => { if (value.Orders.TryGetValue(orderId, out var order) && order.Status == TicketOrderStatus.Pending) order.Status = TicketOrderStatus.Cancelled; });
    public async Task<IReadOnlyList<IssuedTicket>> GetTickets(string storeId) => (await stores.GetSettingAsync<IssuedTicketCollection>(storeId, EventTicketsPlugin.TicketsKey) ?? new()).Tickets.Values.OrderByDescending(t => t.IssuedAt).ToList();
    public async Task<IssuedTicket?> GetTicket(string storeId, string id) => (await stores.GetSettingAsync<IssuedTicketCollection>(storeId, EventTicketsPlugin.TicketsKey) ?? new()).Tickets.GetValueOrDefault(id);
    public async Task<IssuedTicket?> FindTicketByCodeHash(string storeId, string hash) => (await GetTickets(storeId)).FirstOrDefault(t => t.CodeHash == hash);
    public async Task SaveTickets(string storeId, IEnumerable<IssuedTicket> tickets) => await Mutate<IssuedTicketCollection>(storeId, EventTicketsPlugin.TicketsKey, value => { foreach (var ticket in tickets) value.Tickets[ticket.Id] = ticket; });
    public static bool IsCurrentlyInside(IssuedTicket ticket) =>
        ticket.CheckedInAt is not null &&
        (ticket.CheckedOutAt is null || ticket.CheckedInAt > ticket.CheckedOutAt);

    public static long EffectiveEntranceCount(IssuedTicket ticket) =>
        Math.Max(ticket.EntranceCount, ticket.CheckedInAt is null ? 0L : 1L);

    public async Task<CheckInResult> LookupTicket(string storeId, TicketEvent item, string rawCode)
    {
        var tickets = new IssuedTicketCollection();
        foreach (var ticket in await GetTickets(storeId)) tickets.Tickets[ticket.Id] = ticket;
        return ApplyLookup(tickets, item, rawCode);
    }

    public static CheckInResult ApplyLookup(IssuedTicketCollection value, TicketEvent item, string rawCode)
    {
        var resolved = ResolveTicket(value, item, rawCode);
        if (resolved.Error is not null) return resolved.Error;
        var ticket = resolved.Ticket!;
        var inside = IsCurrentlyInside(ticket);
        return Result(ticket, item, resolved.TicketType!, true, inside ? "inside" : "ready_check_in",
            inside ? "Ticket holder is currently inside." : "Ticket is valid and ready to check in.");
    }

    public async Task<CheckInResult> CheckIn(
        string storeId,
        TicketEvent item,
        string rawCode,
        string user,
        string? gate,
        bool? idCheckConfirmed = null,
        string? operationId = null)
    {
        CheckInResult result = NotFound();
        await MutateIfChanged<IssuedTicketCollection>(storeId, EventTicketsPlugin.TicketsKey, value =>
        {
            result = ApplyCheckIn(value, item, rawCode, user, gate, idCheckConfirmed, operationId, out var changed);
            return changed;
        });
        return result;
    }

    public async Task<CheckInResult> CheckOut(
        string storeId,
        TicketEvent item,
        string rawCode,
        string user,
        string? gate,
        string? operationId = null)
    {
        CheckInResult result = NotFound();
        await MutateIfChanged<IssuedTicketCollection>(storeId, EventTicketsPlugin.TicketsKey, value =>
        {
            result = ApplyCheckOut(value, item, rawCode, user, gate, operationId, out var changed);
            return changed;
        });
        return result;
    }

    public async Task<CheckInResult> ApplyScannerAction(
        string storeId,
        TicketEvent item,
        ScannerActionRequest request,
        string user)
    {
        var action = request.Action?.Trim().ToLowerInvariant();
        return action switch
        {
            "check_in" => await CheckIn(storeId, item, request.Code, user, request.Gate,
                request.IdConfirmed, request.OperationId),
            "check_out" => await CheckOut(storeId, item, request.Code, user, request.Gate,
                request.OperationId),
            "reject_id" when item.RequireIdCheck => await CheckIn(storeId, item, request.Code, user,
                request.Gate, false, request.OperationId),
            _ => new CheckInResult(false, "invalid_action", null, null, null, null,
                "Choose check in or check out.")
        };
    }

    // Compatibility overload used by older integrations and existing tests.
    public static CheckInResult ApplyCheckIn(
        IssuedTicketCollection value,
        TicketEvent item,
        string rawCode,
        string user,
        string? gate,
        DateTimeOffset? now = null) =>
        ApplyCheckIn(value, item, rawCode, user, gate, null, null, out _, now);

    public static CheckInResult ApplyCheckIn(
        IssuedTicketCollection value,
        TicketEvent item,
        string rawCode,
        string user,
        string? gate,
        bool? idCheckConfirmed,
        string? operationId,
        out bool changed,
        DateTimeOffset? now = null)
    {
        changed = false;
        var resolved = ResolveTicket(value, item, rawCode);
        if (resolved.Error is not null) return resolved.Error;
        var ticket = resolved.Ticket!;
        var ticketType = resolved.TicketType!;

        if (IsCurrentlyInside(ticket))
            return Result(ticket, item, ticketType, false, "duplicate", "Ticket holder is already checked in.");

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var normalizedOperationId = NormalizeOperationId(operationId);
        if (item.RequireIdCheck)
        {
            if (idCheckConfirmed is null)
                return Result(ticket, item, ticketType, false, "id_check_required",
                    "Confirm the attendee ID before checking in.");

            if (idCheckConfirmed == false)
            {
                // A retried browser request must not inflate the rejection audit counter.
                if (normalizedOperationId is not null &&
                    normalizedOperationId.Equals(ticket.LastScannerOperationId, StringComparison.Ordinal))
                    return Result(ticket, item, ticketType, false, "id_rejected",
                        "ID check rejected. Ticket holder remains outside.");

                ticket.IdRejectedCount++;
                ticket.LastIdCheckedAt = timestamp;
                ticket.LastIdCheckedBy = user;
                ticket.LastIdCheckConfirmed = false;
                ticket.LastScannerOperationId = normalizedOperationId;
                changed = true;
                return Result(ticket, item, ticketType, false, "id_rejected",
                    "ID check rejected. Ticket holder remains outside.");
            }

            ticket.IdConfirmedCount++;
            ticket.LastIdCheckedAt = timestamp;
            ticket.LastIdCheckedBy = user;
            ticket.LastIdCheckConfirmed = true;
        }

        ticket.EntranceCount = EffectiveEntranceCount(ticket) + 1;
        ticket.CheckedInAt = timestamp;
        ticket.CheckedInBy = user;
        ticket.CheckInGate = NormalizeGate(gate);
        ticket.LastScannerOperationId = normalizedOperationId;
        changed = true;
        return Result(ticket, item, ticketType, true, "checked_in", "Welcome — ticket checked in.");
    }

    public static CheckInResult ApplyCheckOut(
        IssuedTicketCollection value,
        TicketEvent item,
        string rawCode,
        string user,
        string? gate,
        DateTimeOffset? now = null) =>
        ApplyCheckOut(value, item, rawCode, user, gate, null, out _, now);

    public static CheckInResult ApplyCheckOut(
        IssuedTicketCollection value,
        TicketEvent item,
        string rawCode,
        string user,
        string? gate,
        string? operationId,
        out bool changed,
        DateTimeOffset? now = null)
    {
        changed = false;
        var resolved = ResolveTicket(value, item, rawCode);
        if (resolved.Error is not null) return resolved.Error;
        var ticket = resolved.Ticket!;
        var ticketType = resolved.TicketType!;
        if (!IsCurrentlyInside(ticket))
            return Result(ticket, item, ticketType, false, "not_checked_in",
                "Ticket holder is already outside.");

        ticket.EntranceCount = EffectiveEntranceCount(ticket);
        ticket.CheckedOutAt = now ?? DateTimeOffset.UtcNow;
        ticket.CheckedOutBy = user;
        ticket.CheckOutGate = NormalizeGate(gate);
        ticket.LastScannerOperationId = NormalizeOperationId(operationId);
        changed = true;
        return Result(ticket, item, ticketType, true, "checked_out",
            "Ticket holder checked out and can be admitted again later.");
    }

    private static (IssuedTicket? Ticket, string? TicketType, CheckInResult? Error) ResolveTicket(
        IssuedTicketCollection value,
        TicketEvent item,
        string rawCode)
    {
        var hash = TicketCodeService.Hash(TicketCodeService.ExtractCode(rawCode));
        var ticket = value.Tickets.Values.FirstOrDefault(candidate => candidate.CodeHash == hash);
        if (ticket is null) return (null, null, NotFound());
        if (!ticket.EventId.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
            return (null, null, new CheckInResult(false, "wrong_event", null, null, null, null,
                "This ticket belongs to another event."));

        var ticketType = item.TicketTypes.FirstOrDefault(type =>
            type.Id.Equals(ticket.TicketTypeId, StringComparison.OrdinalIgnoreCase))?.Name ?? "Event ticket";
        if (ticket.Revoked)
            return (ticket, ticketType, Result(ticket, item, ticketType, false, "revoked", "Ticket is revoked."));
        return (ticket, ticketType, null);
    }

    private static CheckInResult Result(
        IssuedTicket ticket,
        TicketEvent item,
        string ticketType,
        bool success,
        string status,
        string message)
    {
        var inside = IsCurrentlyInside(ticket);
        return new CheckInResult(success, status, ticket.Id, ticket.AttendeeName, ticketType,
            ticket.CheckedInAt, message, ticket.CheckedOutAt, inside, item.RequireIdCheck,
            EffectiveEntranceCount(ticket), ticket.IdConfirmedCount, ticket.IdRejectedCount,
            inside ? ticket.CheckInGate : ticket.CheckOutGate);
    }

    private static CheckInResult NotFound() =>
        new(false, "not_found", null, null, null, null, "Ticket not found for this event.");

    private static string? NormalizeGate(string? gate)
    {
        var normalized = gate?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized[..Math.Min(80, normalized.Length)];
    }

    private static string? NormalizeOperationId(string? operationId)
    {
        var normalized = operationId?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized[..Math.Min(100, normalized.Length)];
    }
    public async Task<Dictionary<string, int?>> GetRemaining(string storeId, TicketEvent item)
    {
        var now = DateTimeOffset.UtcNow;
        var orders = (await GetOrders(storeId)).Where(order => order.EventId == item.Id && TicketReservationPolicy.HoldsInventory(order, now)).ToList();
        return CalculateRemaining(item, orders);
    }
    public async Task<IReadOnlyDictionary<string, Dictionary<string, int?>>> GetRemaining(string storeId, IReadOnlyList<TicketEvent> items)
    {
        var now = DateTimeOffset.UtcNow;
        var orders = (await GetOrders(storeId)).Where(order => TicketReservationPolicy.HoldsInventory(order, now)).ToList();
        return items.ToDictionary(item => item.Id, item => CalculateRemaining(item, orders.Where(order => order.EventId == item.Id)));
    }
    private static Dictionary<string, int?> CalculateRemaining(TicketEvent item, IEnumerable<TicketOrder> orders) =>
        item.TicketTypes.ToDictionary(type => type.Id, type => type.Capacity == 0
            ? (int?)null
            : Math.Max(0, type.Capacity - orders.SelectMany(order => TicketCheckoutService.ResolveLines(order, item)).Where(line => line.TicketTypeId == type.Id).Sum(line => line.Quantity)));
    private async Task Mutate<T>(string storeId, string key, Action<T> change) where T : class, new()
    {
        var gate = _locks.GetOrAdd(storeId + ":" + key, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(); try { var value = await stores.GetSettingAsync<T>(storeId, key) ?? new(); change(value); await stores.UpdateSetting(storeId, key, value); } finally { gate.Release(); }
    }
    private async Task MutateIfChanged<T>(string storeId, string key, Func<T, bool> change) where T : class, new()
    {
        var gate = _locks.GetOrAdd(storeId + ":" + key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var value = await stores.GetSettingAsync<T>(storeId, key) ?? new();
            if (change(value)) await stores.UpdateSetting(storeId, key, value);
        }
        finally
        {
            gate.Release();
        }
    }
}

public static class TicketEventSalePolicy
{
    public static bool CanStartCheckout(TicketEvent item, DateTimeOffset now) =>
        item.Published && item.EndsAt > now;
}

public static class TicketReservationPolicy
{
    public static bool CanExpire(TicketOrder order, DateTimeOffset now) =>
        order.Status == TicketOrderStatus.Pending && string.IsNullOrWhiteSpace(order.InvoiceId) && order.ReservationExpiresAt <= now;

    public static bool HoldsInventory(TicketOrder order, DateTimeOffset now) =>
        order.Status == TicketOrderStatus.Paid ||
        order.Status == TicketOrderStatus.Pending && (!string.IsNullOrWhiteSpace(order.InvoiceId) || order.ReservationExpiresAt > now);
}
