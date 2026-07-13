#nullable enable
using System.Collections.Concurrent;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class EventTicketRepository(StoreRepository stores)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    public async Task<EventTicketSettings> GetSettings(string storeId) => await stores.GetSettingAsync<EventTicketSettings>(storeId, EventTicketsPlugin.SettingsKey) ?? new();
    public Task SaveSettings(string storeId, EventTicketSettings value) => stores.UpdateSetting(storeId, EventTicketsPlugin.SettingsKey, value);
    public async Task<IReadOnlyList<TicketEvent>> GetEvents(string storeId) => (await stores.GetSettingAsync<TicketEventCollection>(storeId, EventTicketsPlugin.EventsKey) ?? new()).Events.OrderBy(e => e.StartsAt).ToList();
    public async Task<TicketEvent?> GetEvent(string storeId, string idOrSlug) => (await GetEvents(storeId)).FirstOrDefault(e => e.Id.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase) || e.Slug.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase));
    public async Task SaveEvent(string storeId, TicketEvent item) => await Mutate<TicketEventCollection>(storeId, EventTicketsPlugin.EventsKey, value =>
    {
        if (value.Events.Any(e => e.Id != item.Id && e.Slug.Equals(item.Slug, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Event slug already exists.");
        var index = value.Events.FindIndex(e => e.Id == item.Id); item.UpdatedAt = DateTimeOffset.UtcNow; if (index < 0) value.Events.Add(item); else value.Events[index] = item;
    });
    public async Task<IReadOnlyList<TicketOrder>> GetOrders(string storeId) => (await stores.GetSettingAsync<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey) ?? new()).Orders.Values.OrderByDescending(o => o.CreatedAt).ToList();
    public async Task<TicketOrder?> GetOrder(string storeId, string id) => (await stores.GetSettingAsync<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey) ?? new()).Orders.GetValueOrDefault(id);
    public async Task<TicketOrder?> TryCreateOrder(string storeId, TicketEvent item, EventTicketSettings settings, IReadOnlyList<TicketOrderLine> lines)
    {
        TicketOrder? result = null;
        await Mutate<TicketOrderCollection>(storeId, EventTicketsPlugin.OrdersKey, orders =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var stale in orders.Orders.Values.Where(order => order.Status == TicketOrderStatus.Pending && order.ReservationExpiresAt <= now)) stale.Status = TicketOrderStatus.Cancelled;
            if (lines.Count == 0) return;
            foreach (var line in lines)
            {
                var type = item.TicketTypes.FirstOrDefault(ticketType => ticketType.Id == line.TicketTypeId && ticketType.Active);
                if (type is null || line.Quantity < 1 || line.Quantity > type.MaxPerOrder) return;
                var reserved = orders.Orders.Values
                    .Where(order => order.EventId == item.Id && order.Status != TicketOrderStatus.Cancelled)
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
    public async Task<CheckInResult> CheckIn(string storeId, string rawCode, string user, string? gate)
    {
        CheckInResult result = new(false, "not_found", null, null, null, null, "Ticket not found."); var hash = TicketCodeService.Hash(TicketCodeService.ExtractCode(rawCode));
        await Mutate<IssuedTicketCollection>(storeId, EventTicketsPlugin.TicketsKey, value =>
        {
            var ticket = value.Tickets.Values.FirstOrDefault(t => t.CodeHash == hash); if (ticket is null) return;
            if (ticket.Revoked) { result = new(false, "revoked", ticket.Id, ticket.AttendeeName, ticket.TicketTypeId, ticket.CheckedInAt, "Ticket is revoked."); return; }
            if (ticket.CheckedInAt is not null) { result = new(false, "duplicate", ticket.Id, ticket.AttendeeName, ticket.TicketTypeId, ticket.CheckedInAt, "Ticket was already checked in."); return; }
            ticket.CheckedInAt = DateTimeOffset.UtcNow; ticket.CheckedInBy = user; ticket.CheckInGate = gate; result = new(true, "checked_in", ticket.Id, ticket.AttendeeName, ticket.TicketTypeId, ticket.CheckedInAt, "Welcome — ticket checked in.");
        });
        return result;
    }
    public async Task<Dictionary<string, int?>> GetRemaining(string storeId, TicketEvent item)
    {
        var now = DateTimeOffset.UtcNow;
        var orders = (await GetOrders(storeId)).Where(order => order.EventId == item.Id && order.Status != TicketOrderStatus.Cancelled && (order.Status == TicketOrderStatus.Paid || order.ReservationExpiresAt > now)).ToList();
        return item.TicketTypes.ToDictionary(type => type.Id, type => type.Capacity == 0
            ? (int?)null
            : Math.Max(0, type.Capacity - orders.SelectMany(order => TicketCheckoutService.ResolveLines(order, item)).Where(line => line.TicketTypeId == type.Id).Sum(line => line.Quantity)));
    }
    private async Task Mutate<T>(string storeId, string key, Action<T> change) where T : class, new()
    {
        var gate = _locks.GetOrAdd(storeId + ":" + key, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(); try { var value = await stores.GetSettingAsync<T>(storeId, key) ?? new(); change(value); await stores.UpdateSetting(storeId, key, value); } finally { gate.Release(); }
    }
}
