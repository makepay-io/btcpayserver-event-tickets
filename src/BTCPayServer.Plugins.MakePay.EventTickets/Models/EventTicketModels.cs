#nullable enable
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Models;

public enum TicketEmailProvider { BtcpaySmtp, Resend }
public enum AnalyticsProvider
{
    [Display(Name = "Disabled (dataLayer only)")] Disabled,
    [Display(Name = "Google Tag Manager")] GoogleTagManager,
    [Display(Name = "Google Analytics 4")] GoogleAnalytics
}
public enum TicketFontStyle
{
    [Display(Name = "System sans")] System,
    [Display(Name = "Modern sans")] Modern,
    [Display(Name = "Rounded sans")] Rounded,
    [Display(Name = "Editorial serif")] Editorial,
    [Display(Name = "Neo-grotesk sans")] Grotesk
}

public sealed class EventTicketSettings
{
    private string? _faviconUrl;

    [Required, StringLength(80)] public string StorefrontTitle { get; set; } = "Events";
    [StringLength(500)] public string StorefrontDescription { get; set; } = "Tickets paid directly through BTCPay Server.";
    [Required, StringLength(10)] public string Currency { get; set; } = "USD";
    [Url, StringLength(500)] public string? LogoUrl { get; set; }
    [Url, StringLength(500), RegularExpression("(?i)^https?://[^\\s]+$", ErrorMessage = "Use an absolute HTTP or HTTPS favicon URL.")]
    public string? FaviconUrl
    {
        get => _faviconUrl;
        set => _faviconUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    [Url, StringLength(500)] public string? HeroImageUrl { get; set; }
    [StringLength(80)] public string HeroEyebrow { get; set; } = "MakePay Events";
    [StringLength(180)] public string HeroHeadline { get; set; } = "Built for unforgettable experiences";
    [StringLength(300)] public string HeroSubheadline { get; set; } = "Reserve your place and pay directly with BTCPay Server.";
    [StringLength(120)] public string TicketPageTitle { get; set; } = "Choose your experience";
    [StringLength(240)] public string TicketPageSubtitle { get; set; } = "Select your tickets. Taxes and discounts are shown before payment.";
    [StringLength(120)] public string ConfirmationTitle { get; set; } = "You are on the list";
    [StringLength(500)] public string ConfirmationMessage { get; set; } = "Your payment is confirmed and your tickets are ready.";
    [StringLength(500)] public string AttendeeNotice { get; set; } = "Attendee details should match a valid ID used at the venue.";
    [StringLength(1000)] public string FooterText { get; set; } = "Your order is processed securely by this BTCPay Server.";
    [Url, StringLength(500)] public string? PrivacyUrl { get; set; }
    [Url, StringLength(500)] public string? TermsUrl { get; set; }

    public AnalyticsProvider AnalyticsProvider { get; set; }
    [StringLength(40), RegularExpression("(?i)^\\s*GTM-[A-Z0-9]+\\s*$", ErrorMessage = "Use a valid Google Tag Manager container ID such as GTM-ABC123.")]
    public string? GoogleTagManagerContainerId { get; set; }
    [StringLength(40), RegularExpression("(?i)^\\s*G-[A-Z0-9]+\\s*$", ErrorMessage = "Use a valid GA4 measurement ID such as G-ABC123DEF4.")]
    public string? GoogleAnalyticsMeasurementId { get; set; }
    public bool RequireAnalyticsConsent { get; set; } = true;
    public bool RespectDoNotTrack { get; set; } = true;

    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; set; } = "#155EEF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentTextColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string BrandTextColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string PageBackgroundColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string SurfaceColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string TextColor { get; set; } = "#101828";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string MutedColor { get; set; } = "#667085";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string TimerColor { get; set; } = "#FEC84B";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string TimerTextColor { get; set; } = "#101828";
    public TicketFontStyle FontStyle { get; set; } = TicketFontStyle.Modern;
    [Range(35, 60)] public int BrandPanelWidth { get; set; } = 50;
    [Range(5, 60)] public int CheckoutMinutes { get; set; } = 15;
    [Range(0, 60)] public int ScannerResultSeconds { get; set; } = 5;
    public bool ShowCountdown { get; set; } = true;
    public bool RequirePhone { get; set; } = true;
    public bool RequireCountry { get; set; } = true;
    public bool CollectCompany { get; set; }

    [StringLength(80)] public string? PromoCode { get; set; }
    [Range(0, 99)] public decimal PromoPercent { get; set; }
    [StringLength(240)] public string PromoDescription { get; set; } = "Enter your event code to unlock a discount.";

    public bool DeliverOnProcessing { get; set; }
    public TicketEmailProvider EmailProvider { get; set; }
    public string? ProtectedResendApiKey { get; set; }
    [StringLength(200)] public string? ResendFrom { get; set; }
    [StringLength(200)] public string EmailSubject { get; set; } = "Your tickets for {EventName}";
    public string EmailHtml { get; set; } = "<p>Your tickets for <strong>{EventName}</strong> are ready.</p><p>{EventDate}<br>{Venue}</p><p><a href=\"{OrderUrl}\">View tickets and wallet passes</a></p>";
    public bool AttachPdf { get; set; } = true;
    [StringLength(100)] public string? ApplePassTypeIdentifier { get; set; }
    [StringLength(100)] public string? AppleTeamIdentifier { get; set; }
    [StringLength(100)] public string AppleOrganizationName { get; set; } = "MakePay Tickets";
    public string? ProtectedAppleP12 { get; set; }
    public string? ProtectedAppleP12Password { get; set; }
    [StringLength(100)] public string? GoogleIssuerId { get; set; }
    [StringLength(200)] public string? GoogleClassId { get; set; }
    public string? ProtectedGoogleServiceAccountJson { get; set; }
    public bool ShowMakePayPromotion { get; set; } = true;
    [StringLength(200)] public string PromotionText { get; set; } = "Created by MakePay.io — accept 90+ currencies in a decentralized way with BTCPay Server.";
}

public sealed class TicketType
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(100)] public string Name { get; set; } = "General admission";
    [StringLength(1000)] public string Description { get; set; } = "";
    [StringLength(80)] public string? Badge { get; set; }
    [Range(0.00000001, 1000000000)] public decimal Price { get; set; }
    [Range(0.00000001, 1000000000)] public decimal? CompareAtPrice { get; set; }
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
    [Url, StringLength(500)] public string? BannerUrl { get; set; }
    public bool Published { get; set; } = true;
    public bool RequireIdCheck { get; set; }
    public List<TicketType> TicketTypes { get; set; } = [];
    public string? ProtectedScannerAccessToken { get; set; }
    public string? ScannerAccessTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TicketEventCollection { public List<TicketEvent> Events { get; set; } = []; }
public enum TicketOrderStatus { Pending, Paid, Cancelled }

public sealed class TicketOrderLine
{
    public string TicketTypeId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class TicketAttendee
{
    public string TicketTypeId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Country { get; set; } = "";
    public string Company { get; set; } = "";
}

public sealed class TicketOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string EventId { get; set; } = "";
    // Legacy single-line fields remain for orders created before v1.1.
    public string TicketTypeId { get; set; } = "";
    public int Quantity { get; set; }
    public List<TicketOrderLine> Lines { get; set; } = [];
    public List<TicketAttendee> Attendees { get; set; } = [];
    public string BuyerEmail { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string BuyerFirstName { get; set; } = "";
    public string BuyerLastName { get; set; } = "";
    public string BuyerPhone { get; set; } = "";
    public string BuyerCountry { get; set; } = "";
    public string BuyerCompany { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
    public string? PromoCode { get; set; }
    public string? InvoiceId { get; set; }
    public bool PosMode { get; set; }
    public string PublicBaseUrl { get; set; } = "";
    // Null preserves URL behavior for orders created before route/origin
    // intent was persisted. New orders always store an explicit value.
    public bool? PreferCleanUrls { get; set; }
    public string? ProtectedPublicAccessToken { get; set; }
    public string? PublicAccessTokenHash { get; set; }
    public TicketOrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReservationExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);
    public DateTimeOffset? TermsAcceptedAt { get; set; }
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
    public DateTimeOffset? CheckedOutAt { get; set; }
    public string? CheckedOutBy { get; set; }
    public string? CheckOutGate { get; set; }
    public long EntranceCount { get; set; }
    public long IdConfirmedCount { get; set; }
    public long IdRejectedCount { get; set; }
    public DateTimeOffset? LastIdCheckedAt { get; set; }
    public string? LastIdCheckedBy { get; set; }
    public bool? LastIdCheckConfirmed { get; set; }
    public string? LastScannerOperationId { get; set; }
    public bool Revoked { get; set; }
}

public sealed class IssuedTicketCollection { public Dictionary<string, IssuedTicket> Tickets { get; set; } = new(StringComparer.OrdinalIgnoreCase); }

public sealed class TicketSelectionInput
{
    public Dictionary<string, int> Quantities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Pos { get; set; }
}

public sealed class TicketAttendeeInput
{
    public string TicketTypeId { get; set; } = "";
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [StringLength(100)] public string? Nickname { get; set; }
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(160)] public string? Company { get; set; }
}

public sealed class TicketCheckoutInput
{
    [Required, StringLength(100)] public string BuyerFirstName { get; set; } = "";
    [Required, StringLength(100)] public string BuyerLastName { get; set; } = "";
    [Required, EmailAddress, StringLength(200)] public string BuyerEmail { get; set; } = "";
    [StringLength(50)] public string? BuyerPhone { get; set; }
    [StringLength(100)] public string? BuyerCountry { get; set; }
    [StringLength(160)] public string? BuyerCompany { get; set; }
    public bool AcceptTerms { get; set; }
    public List<TicketAttendeeInput> Attendees { get; set; } = [];
}

public sealed class TicketLineViewModel
{
    public required TicketType TicketType { get; init; }
    public required TicketOrderLine Line { get; init; }
    public decimal Total => Line.UnitPrice * Line.Quantity;
}

public sealed class EventTicketsDashboardViewModel
{
    public required string StoreId { get; init; }
    public required EventTicketSettings Settings { get; init; }
    public required IReadOnlyList<TicketEvent> Events { get; init; }
    public required EventTicketOrderPage Orders { get; init; }
    public required IReadOnlyList<IssuedTicket> Tickets { get; init; }
    public required IReadOnlyDictionary<string, string> ScannerAccessTokens { get; init; }
    public string? MappedBaseUrl { get; init; }
}

public sealed class EventTicketOrderQuery
{
    public string? OrderSearch { get; set; }
    public string? OrderEventId { get; set; }
    public string? OrderStatus { get; set; }
    public int OrderPage { get; set; } = 1;
    public int OrderPageSize { get; set; } = 25;
}

public sealed class EventTicketOrderPage
{
    public required IReadOnlyList<TicketOrder> Items { get; init; }
    public string? Search { get; init; }
    public string? EventId { get; init; }
    public TicketOrderStatus? Status { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalItems { get; init; }
    public required int TotalPages { get; init; }
    public int FirstItem => TotalItems == 0 ? 0 : ((Page - 1) * PageSize) + 1;
    public int LastItem => Math.Min(Page * PageSize, TotalItems);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) ||
                              !string.IsNullOrWhiteSpace(EventId) ||
                              Status is not null;
}

public sealed class EventTicketOrderLineDetail
{
    public required string TicketTypeId { get; init; }
    public required string TicketTypeName { get; init; }
    public required int Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? Total => UnitPrice * Quantity;
}

public sealed record EventTicketInvoiceSnapshot(decimal Amount, string Currency);

public sealed class EventTicketAttendeeDetail
{
    public required string TicketTypeId { get; init; }
    public required string TicketTypeName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Nickname { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required string Country { get; init; }
    public required string Company { get; init; }
    public string Name => string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class EventTicketIssuedTicketDetail
{
    public required string TicketId { get; init; }
    public required string TicketTypeId { get; init; }
    public required string TicketTypeName { get; init; }
    public required string AttendeeName { get; init; }
    public required string AttendeeEmail { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? CheckedInAt { get; init; }
    public string? CheckedInBy { get; init; }
    public string? CheckInGate { get; init; }
    public DateTimeOffset? CheckedOutAt { get; init; }
    public string? CheckedOutBy { get; init; }
    public string? CheckOutGate { get; init; }
    public required long EntranceCount { get; init; }
    public required long IdConfirmedCount { get; init; }
    public required long IdRejectedCount { get; init; }
    public DateTimeOffset? LastIdCheckedAt { get; init; }
    public string? LastIdCheckedBy { get; init; }
    public bool? LastIdCheckConfirmed { get; init; }
    public required bool Revoked { get; init; }
    public required bool IsInside { get; init; }
    public required DateTimeOffset LastActivityAt { get; init; }
    public string? LastGate { get; init; }
}

public sealed class EventTicketAdminOrderDetail
{
    public required string Id { get; init; }
    public required string EventId { get; init; }
    public required string BuyerEmail { get; init; }
    public required string BuyerName { get; init; }
    public required string BuyerFirstName { get; init; }
    public required string BuyerLastName { get; init; }
    public required string BuyerPhone { get; init; }
    public required string BuyerCountry { get; init; }
    public required string BuyerCompany { get; init; }
    public string? Currency { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? DiscountAmount { get; init; }
    public decimal? Total { get; init; }
    public required bool AmountsFromInvoice { get; init; }
    public string? PromoCode { get; init; }
    public string? InvoiceId { get; init; }
    public required bool PosMode { get; init; }
    public required TicketOrderStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReservationExpiresAt { get; init; }
    public DateTimeOffset? TermsAcceptedAt { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public required bool DeliverySent { get; init; }
}

public sealed class EventTicketAdminEventDetail
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string VenueName { get; init; }
    public required string VenueAddress { get; init; }
    public required DateTimeOffset StartsAt { get; init; }
    public required DateTimeOffset EndsAt { get; init; }
    public required bool RequireIdCheck { get; init; }
}

public sealed class EventTicketOrderDetailViewModel
{
    public required string StoreId { get; init; }
    public required EventTicketAdminOrderDetail Order { get; init; }
    public EventTicketAdminEventDetail? Event { get; init; }
    public required IReadOnlyList<EventTicketOrderLineDetail> Lines { get; init; }
    public required IReadOnlyList<EventTicketAttendeeDetail> Attendees { get; init; }
    public required IReadOnlyList<EventTicketIssuedTicketDetail> Tickets { get; init; }
    public EventTicketOrderQuery ReturnQuery { get; init; } = new();
}

public sealed class EventStorefrontViewModel { public required string StoreId { get; init; } public required EventTicketSettings Settings { get; init; } public required IReadOnlyList<TicketEvent> Events { get; init; } public required IReadOnlyDictionary<string, Dictionary<string, int?>> Remaining { get; init; } public bool CleanUrls { get; init; } }
public sealed class EventDetailViewModel { public required string StoreId { get; init; } public required EventTicketSettings Settings { get; init; } public required TicketEvent Event { get; init; } public required Dictionary<string, int?> Remaining { get; init; } public bool PosMode { get; init; } public bool CleanUrls { get; init; } }
public sealed class EventScannerViewModel
{
    public required string StoreId { get; init; }
    public required EventTicketSettings Settings { get; init; }
    public required TicketEvent Event { get; init; }
    public required string LookupUrl { get; init; }
    public required string ActionUrl { get; init; }
    public required string CheckInUrl { get; init; }
    public required string EventUrl { get; init; }
    public bool CleanUrls { get; init; }
}

public sealed class EventTicketAnalyticsContext
{
    public required EventTicketSettings Settings { get; init; }
    public required string StoreId { get; init; }
    public required string PageType { get; init; }
    public string? EventId { get; init; }
    public string? EventSlug { get; init; }
}

public sealed class TicketCheckoutPageViewModel
{
    public required string StoreId { get; init; }
    public required EventTicketSettings Settings { get; init; }
    public required TicketEvent Event { get; init; }
    public required TicketOrder Order { get; init; }
    public required IReadOnlyList<TicketLineViewModel> Lines { get; init; }
    public required string AccessToken { get; init; }
    public TicketCheckoutInput Input { get; init; } = new();
    public int Step { get; init; }
    public string? PromoMessage { get; init; }
    public bool CleanUrls { get; init; }
}

public sealed class DisplayTicket
{
    public required IssuedTicket Ticket { get; init; }
    public required TicketType TicketType { get; init; }
    public required string Code { get; init; }
    public required string QrDataUri { get; init; }
    public string? AppleWalletUrl { get; init; }
    public string? GoogleWalletUrl { get; init; }
}

public sealed class TicketOrderViewModel
{
    public required EventTicketSettings Settings { get; init; }
    public required TicketEvent Event { get; init; }
    public required TicketOrder Order { get; init; }
    public required IReadOnlyList<TicketLineViewModel> Lines { get; init; }
    public required IReadOnlyList<DisplayTicket> Tickets { get; init; }
    public required string AccessToken { get; init; }
    public string? PdfUrl { get; init; }
    public bool CleanUrls { get; init; }
}

public sealed record TicketPaymentStatus(string Status, string? RedirectUrl, string Message);
public sealed record CheckInResult(
    bool Success,
    string Status,
    string? TicketId,
    string? Attendee,
    string? TicketType,
    DateTimeOffset? CheckedInAt,
    string Message,
    DateTimeOffset? CheckedOutAt = null,
    bool IsInside = false,
    bool RequireIdCheck = false,
    long EntranceCount = 0,
    long IdConfirmedCount = 0,
    long IdRejectedCount = 0,
    string? LastGate = null);
public sealed record ScannerLookupRequest(string Code);
public sealed record ScannerCheckInRequest(string Code, string? Gate, bool? IdCheckConfirmed = null, string? OperationId = null);
public sealed record ScannerCheckOutRequest(string Code, string? Gate, string? OperationId = null);
public sealed record ScannerActionRequest(string Code, string? Gate, string Action, bool? IdConfirmed = null, string? OperationId = null);
