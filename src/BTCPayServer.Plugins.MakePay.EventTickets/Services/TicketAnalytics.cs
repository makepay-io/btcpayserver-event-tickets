#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class TicketAnalyticsItem
{
    [JsonPropertyName("item_id")] public required string ItemId { get; init; }
    [JsonPropertyName("item_name")] public required string ItemName { get; init; }
    [JsonPropertyName("item_variant")] public required string ItemVariant { get; init; }
    [JsonPropertyName("ticket_type_id")] public required string TicketTypeId { get; init; }
    [JsonPropertyName("item_category")] public string ItemCategory { get; init; } = "Event tickets";
    [JsonPropertyName("item_list_id")] public required string ItemListId { get; init; }
    [JsonPropertyName("item_list_name")] public required string ItemListName { get; init; }
    [JsonPropertyName("price")] public decimal Price { get; init; }
    [JsonPropertyName("discount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public decimal Discount { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; } = 1;
}

public sealed class TicketAnalyticsEcommerce
{
    [JsonPropertyName("currency")] public required string Currency { get; init; }
    [JsonPropertyName("value")] public decimal Value { get; init; }
    [JsonPropertyName("items")] public required IReadOnlyList<TicketAnalyticsItem> Items { get; init; }
    [JsonPropertyName("transaction_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TransactionId { get; init; }
    [JsonPropertyName("coupon"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Coupon { get; init; }
    [JsonPropertyName("payment_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PaymentType { get; init; }
    [JsonPropertyName("item_list_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ItemListId { get; init; }
    [JsonPropertyName("item_list_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ItemListName { get; init; }
}

public static class TicketAnalytics
{
    public const string ConsentGrantedEvent = "makepay:analytics-consent-granted";
    public static readonly IReadOnlyList<string> GoogleScriptSources = ["https://*.googletagmanager.com"];
    public static readonly IReadOnlyList<string> GoogleConnectSources =
    [
        "'self'",
        "https://*.google-analytics.com",
        "https://*.analytics.google.com",
        "https://*.googletagmanager.com"
    ];

    public static IReadOnlyList<TicketAnalyticsItem> ForEvent(TicketEvent item, IEnumerable<TicketType> ticketTypes) =>
        ticketTypes.Select(type => CreateItem(item, type, type.Price, 1)).ToList();

    public static IReadOnlyList<TicketAnalyticsItem> ForStorefront(IEnumerable<TicketEvent> events) =>
        events.SelectMany(item => item.TicketTypes.Where(type => type.Active).Select(type => CreateItem(item, type, type.Price, 1))).ToList();

    public static TicketAnalyticsEcommerce ForOrder(TicketOrder order, TicketEvent item, IEnumerable<TicketLineViewModel> lines, bool purchase = false, bool payment = false)
    {
        var analyticsItems = lines.Select(line =>
        {
            var unitDiscount = order.Subtotal > 0 && order.DiscountAmount > 0
                ? line.Line.UnitPrice * order.DiscountAmount / order.Subtotal
                : 0m;
            return CreateItem(item, line.TicketType, line.Line.UnitPrice - unitDiscount, line.Line.Quantity, unitDiscount);
        }).ToList();
        return new TicketAnalyticsEcommerce
        {
            Currency = order.Currency.ToUpperInvariant(),
            Value = order.Total,
            Items = analyticsItems,
            TransactionId = purchase ? AnalyticsTransactionId(order) : null,
            Coupon = string.IsNullOrWhiteSpace(order.PromoCode) ? null : order.PromoCode,
            PaymentType = payment ? "BTCPay Server" : null,
            ItemListId = item.Id,
            ItemListName = item.Name
        };
    }

    public static string AnalyticsTransactionId(TicketOrder order)
    {
        var source = Encoding.UTF8.GetBytes($"event_tickets\n{order.StoreId}\n{order.Id}");
        return $"mpt_{Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant()}";
    }

    private static TicketAnalyticsItem CreateItem(TicketEvent item, TicketType type, decimal price, int quantity, decimal discount = 0m) => new()
    {
        ItemId = item.Id,
        ItemName = item.Name,
        ItemVariant = type.Name,
        TicketTypeId = type.Id,
        ItemListId = item.Id,
        ItemListName = item.Name,
        Price = price,
        Discount = discount,
        Quantity = quantity
    };
}
