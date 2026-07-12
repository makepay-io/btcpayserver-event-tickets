#nullable enable
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Models;

public enum TicketEmailProvider { BtcpaySmtp, Resend }
public sealed class EventTicketSettings
{
    [Required, StringLength(80)] public string StorefrontTitle { get; set; } = "Events";
    [StringLength(500)] public string StorefrontDescription { get; set; } = "Tickets paid directly through BTCPay Server.";
    [Required, StringLength(10)] public string Currency { get; set; } = "USD";
    [StringLength(500)] public string? LogoUrl { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; set; } = "#ea580c";
    public bool DeliverOnProcessing { get; set; }
    public TicketEmailProvider EmailProvider { get; set; }
    public string? ProtectedResendApiKey { get; set; }
    [StringLength(200)] public string? ResendFrom { get; set; }
    [StringLength(200)] public string EmailSubject { get; set; } = "Your tickets for {EventName}";
    public string EmailHtml { get; set; } = "<p>Your tickets for <strong>{EventName}</strong> are ready.</p><p>{EventDate}<br>{Venue}</p><p><a href=\"{OrderUrl}\">View tickets and wallet passes</a></p>";
    public bool AttachPdf { get; set; } = true;
    [StringLength(100)] public string ApplePassTypeIdentifier { get; set; } = "";
    [StringLength(100)] public string AppleTeamIdentifier { get; set; } = "";
    [StringLength(100)] public string AppleOrganizationName { get; set; } = "MakePay Tickets";
    public string? ProtectedAppleP12 { get; set; }
    public string? ProtectedAppleP12Password { get; set; }
    [StringLength(100)] public string GoogleIssuerId { get; set; } = "";
    [StringLength(200)] public string GoogleClassId { get; set; } = "";
    public string? ProtectedGoogleServiceAccountJson { get; set; }
    public bool ShowMakePayPromotion { get; set; } = true;
    [StringLength(200)] public string PromotionText { get; set; } = "Created by MakePay.io — accept 90+ currencies in a decentralized way with BTCPay Server.";
}

public sealed class TicketType
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(100)] public string Name { get; set; } = "General admission";
    [StringLength(1000)] public string Description { get; set; } = "";
    [Range(0.00000001, 1000000000)] public decimal Price { get; set; }
    [Range(0, 1000000)] public int Capacity { get; set; } = 100;
    [Range(1, 100)] public int MaxPerOrder { get; set; } = 10;
    public bool Active { get; set; } = true;
}

public sealed class TicketEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(80), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")] public string Slug { get; set; } = "new-event";
    [Required, StringLength(160)] public string Name { get; set; } = "New event";
    [StringLength(6000)] public string Description { get; set; } = "";
    [Required, StringLength(200)] public string VenueName { get; set; } = "Venue";
    [StringLength(500)] public string VenueAddress { get; set; } = "";
    [Required, StringLength(100)] public string TimeZoneId { get; set; } = "UTC";
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
    public DateTimeOffset EndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30).AddHours(3);
    [StringLength(500)] public string? BannerUrl { get; set; }
    public bool Published { get; set; } = true;
    public List<TicketType> TicketTypes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class TicketEventCollection { public List<TicketEvent> Events { get; set; } = []; }
public enum TicketOrderStatus { Pending, Paid, Cancelled }
public sealed class TicketOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string TicketTypeId { get; set; } = "";
    public int Quantity { get; set; }
    public string BuyerEmail { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string? InvoiceId { get; set; }
    public string PublicBaseUrl { get; set; } = "";
    public TicketOrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public List<string> TicketIds { get; set; } = [];
    public bool DeliverySent { get; set; }
}
public sealed class TicketOrderCollection { public Dictionary<string, TicketOrder> Orders { get; set; } = new(StringComparer.OrdinalIgnoreCase); }

public sealed class IssuedTicket
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string TicketTypeId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string AttendeeName { get; set; } = "";
    public string AttendeeEmail { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string ProtectedCode { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CheckedInAt { get; set; }
    public string? CheckedInBy { get; set; }
    public string? CheckInGate { get; set; }
    public bool Revoked { get; set; }
}
public sealed class IssuedTicketCollection { public Dictionary<string, IssuedTicket> Tickets { get; set; } = new(StringComparer.OrdinalIgnoreCase); }

public sealed class EventTicketsDashboardViewModel
{
    public required string StoreId { get; init; }
    public required EventTicketSettings Settings { get; init; }
    public required IReadOnlyList<TicketEvent> Events { get; init; }
    public required IReadOnlyList<TicketOrder> Orders { get; init; }
    public required IReadOnlyList<IssuedTicket> Tickets { get; init; }
}
public sealed class EventStorefrontViewModel { public required string StoreId { get; init; } public required EventTicketSettings Settings { get; init; } public required IReadOnlyList<TicketEvent> Events { get; init; } }
public sealed class EventDetailViewModel { public required string StoreId { get; init; } public required EventTicketSettings Settings { get; init; } public required TicketEvent Event { get; init; } public required Dictionary<string, int?> Remaining { get; init; } public bool PosMode { get; init; } }
public sealed class DisplayTicket { public required IssuedTicket Ticket { get; init; } public required string Code { get; init; } public required string QrDataUri { get; init; } public string? AppleWalletUrl { get; init; } public string? GoogleWalletUrl { get; init; } }
public sealed class TicketOrderViewModel { public required EventTicketSettings Settings { get; init; } public required TicketEvent Event { get; init; } public required TicketType TicketType { get; init; } public required TicketOrder Order { get; init; } public required IReadOnlyList<DisplayTicket> Tickets { get; init; } public string? PdfUrl { get; init; } }
public sealed record CheckInResult(bool Success, string Status, string? TicketId, string? Attendee, string? TicketType, DateTimeOffset? CheckedInAt, string Message);
