#nullable enable
using BTCPayServer.Plugins.MakePay.EventTickets.Models;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public static class EventTicketOrderQueryService
{
    public static IReadOnlyList<int> AllowedPageSizes { get; } = [10, 25, 50, 100];

    public static EventTicketOrderPage Apply(
        IEnumerable<TicketOrder> orders,
        IReadOnlyCollection<TicketEvent> events,
        EventTicketOrderQuery? query)
    {
        query ??= new EventTicketOrderQuery();
        var search = Normalize(query.OrderSearch, 200);
        var requestedEventId = Normalize(query.OrderEventId, 100);
        var eventsById = events
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var eventId = requestedEventId is not null && eventsById.TryGetValue(requestedEventId, out var requestedEvent)
            ? requestedEvent.Id
            : null;
        var status = Enum.TryParse<TicketOrderStatus>(query.OrderStatus, true, out var parsedStatus) &&
                     Enum.IsDefined(typeof(TicketOrderStatus), parsedStatus)
            ? parsedStatus
            : (TicketOrderStatus?)null;
        var pageSize = AllowedPageSizes.Contains(query.OrderPageSize) ? query.OrderPageSize : 25;

        var filtered = orders.Where(order =>
            (eventId is null || string.Equals(order.EventId, eventId, StringComparison.OrdinalIgnoreCase)) &&
            (status is null || order.Status == status) &&
            (search is null || MatchesSearch(order, search, eventsById)));

        var ordered = filtered
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalItems = ordered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        var page = Math.Clamp(query.OrderPage, 1, totalPages);

        return new EventTicketOrderPage
        {
            Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Search = search,
            EventId = eventId,
            Status = status,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    private static bool MatchesSearch(
        TicketOrder order,
        string search,
        IReadOnlyDictionary<string, TicketEvent> eventsById)
    {
        eventsById.TryGetValue(order.EventId, out var item);
        return Contains(order.Id, search) ||
               Contains(order.InvoiceId, search) ||
               Contains(order.BuyerName, search) ||
               Contains(order.BuyerEmail, search) ||
               Contains(item?.Name, search) ||
               Contains(item?.Slug, search) ||
               order.Attendees?.Any(attendee =>
                   Contains($"{attendee.FirstName} {attendee.LastName}", search) ||
                   Contains(attendee.Email, search)) == true;
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Normalize(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
