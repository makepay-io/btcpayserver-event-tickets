#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Tests;

public sealed class ExperienceParityTests
{
    private static readonly string AdminDashboard = Source("Views", "EventTickets", "Index.cshtml");
    private static readonly string Settings = Source("Views", "EventTickets", "Settings.cshtml");
    private static readonly string EventEditor = Source("Views", "EventTickets", "Event.cshtml");
    private static readonly string Cart = Source("Views", "EventTickets", "Public", "Cart.cshtml");
    private static readonly string Details = Source("Views", "EventTickets", "Public", "Details.cshtml");
    private static readonly string Countdown = Source("Views", "EventTickets", "Public", "_Countdown.cshtml");
    private static readonly string Order = Source("Views", "EventTickets", "Public", "Order.cshtml");
    private static readonly string Footer = Source("Views", "EventTickets", "Public", "_Footer.cshtml");
    private static readonly string Scanner = Source("Views", "EventTickets", "Public", "Scanner.cshtml");
    private static readonly string Repository = Source("Services", "EventTicketRepository.cs");
    private static readonly string TicketCodes = Source("Services", "TicketCodeService.cs");
    private static readonly string AdminController = Source("Controllers", "EventTicketsAdminController.cs");
    private static readonly string PublicController = Source("Controllers", "EventTicketsPublicController.cs");

    [Fact]
    public void Admin_dashboard_uses_the_event_management_shell_and_preserves_primary_actions()
    {
        Assert.Contains("class=\"mp-admin-dashboard\"", AdminDashboard, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(AdminDashboard, "class=\"mp-stat-card\"").Count);

        Assert.Contains("Events", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("Recent orders", AdminDashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-action=\"Scanner\"", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Settings\"", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Event\"", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("Add event", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("View store", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("Store", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("POS", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"UIInvoice\"", AdminDashboard, StringComparison.Ordinal);

        Assert.Contains("data-event-search", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("data-event-status", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("is-live", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("is-draft", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("is-paid", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("is-pending", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("is-revoked", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("On sale", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("Ended", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("TicketEventSalePolicy.CanStartCheckout", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("Model.ScannerAccessTokens", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.ScannerPath", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("scannerToken", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains(">Scanner</a>", AdminDashboard, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"RotateScanner\"", AdminDashboard, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\.mp-admin-dashboard\{[^}]*overflow-x\s*:\s*hidden",
            RegexOptions.CultureInvariant), AdminDashboard);
    }

    [Fact]
    public void Scanner_is_an_event_scoped_capability_link_not_an_admin_or_global_route()
    {
        Assert.Contains("GetEventsWithScannerAccess(storeId)", AdminController, StringComparison.Ordinal);
        Assert.Contains("ToDictionary(item => item.Id, secrets.EnsureScannerAccessToken", AdminController,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IActionResult Scanner(string storeId", AdminController, StringComparison.Ordinal);

        var publicUrls = Source("Services", "TicketPublicUrl.cs");
        Assert.Contains("ScannerPath(bool clean, string storeId, string eventSlug, string? scannerToken = null)",
            publicUrls, StringComparison.Ordinal);
        Assert.Contains("EventPath(clean, storeId, eventSlug) + \"/scanner\"", publicUrls,
            StringComparison.Ordinal);
        Assert.Contains("\"?scannerToken=\" + Uri.EscapeDataString(scannerToken)", publicUrls,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_use_five_accessible_tabs_with_keyboard_and_error_aware_navigation()
    {
        Assert.Contains("class=\"mp-settings-dashboard\"", Settings, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", Settings, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(Settings, "role=\"tab\"").Count);
        Assert.Equal(5, Regex.Matches(Settings, "role=\"tabpanel\"").Count);

        foreach (var panel in new[] { "storefront", "checkout", "delivery", "analytics", "domain" })
        {
            Assert.Contains($"data-settings-tab=\"{panel}\"", Settings, StringComparison.Ordinal);
            Assert.Contains($"data-settings-panel=\"{panel}\"", Settings, StringComparison.Ordinal);
            Assert.Contains($"aria-controls=\"settings-panel-{panel}\"", Settings, StringComparison.Ordinal);
            Assert.Contains($"aria-labelledby=\"settings-tab-{panel}\"", Settings, StringComparison.Ordinal);
        }

        Assert.Contains("event.key==='ArrowRight'", Settings, StringComparison.Ordinal);
        Assert.Contains("event.key==='ArrowLeft'", Settings, StringComparison.Ordinal);
        Assert.Contains("event.key==='Home'", Settings, StringComparison.Ordinal);
        Assert.Contains("event.key==='End'", Settings, StringComparison.Ordinal);
        Assert.Contains("invalid?.closest('[data-settings-panel]')", Settings, StringComparison.Ordinal);
        Assert.Contains("form.addEventListener('invalid'", Settings, StringComparison.Ordinal);
        Assert.Contains("sandbox=\"allow-same-origin\"", Settings, StringComparison.Ordinal);
        Assert.Contains("var btcpayLogoUrl = Url.Content(\"~/img/btcpay-logo.svg\")", Settings,
            StringComparison.Ordinal);
        Assert.Contains("btcpayLogo=@Html.Raw", Settings, StringComparison.Ordinal);
        Assert.Contains("escapeHtml(btcpayLogo)", Settings, StringComparison.Ordinal);
        Assert.DoesNotContain("${location.origin}/img/btcpay-logo.svg", Settings, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", Settings, StringComparison.Ordinal);
        Assert.Contains("history.replaceState", Settings, StringComparison.Ordinal);

        Assert.Contains("var iconSpriteUrl = Url.Content(\"~/img/icon-sprite.svg\")", Settings, StringComparison.Ordinal);
        foreach (var icon in new[] { "pos-cart", "actions-email", "nav-explore", "time" })
            Assert.Contains($"{{iconSpriteUrl}}#{icon}", Settings, StringComparison.Ordinal);
        foreach (var missingIcon in new[] { "#cart\"", "#email\"", "#globe\"", "#timer\"" })
            Assert.DoesNotContain(missingIcon, Settings, StringComparison.Ordinal);

        Assert.True(Regex.Matches(Settings, "data-settings-submit").Count >= 2,
            "Both the header and the save bar should submit the same settings form.");
        Assert.Contains("form=\"ticket-settings-form\"", Settings, StringComparison.Ordinal);
        Assert.Contains("form.querySelectorAll('[data-settings-submit]')", Settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_preserve_persisted_bindings_live_editor_and_write_only_credentials()
    {
        var persistedFields = new[]
        {
            "AccentColor", "AccentTextColor", "AnalyticsProvider", "AppleOrganizationName",
            "ApplePassTypeIdentifier", "AppleTeamIdentifier", "AttachPdf", "AttendeeNotice",
            "BrandPanelWidth", "BrandTextColor", "CheckoutMinutes", "CollectCompany",
            "ConfirmationMessage", "ConfirmationTitle", "Currency", "DeliverOnProcessing", "EmailHtml",
            "EmailProvider", "EmailSubject", "FaviconUrl", "FontStyle", "FooterText",
            "GoogleAnalyticsMeasurementId", "GoogleClassId", "GoogleIssuerId", "GoogleTagManagerContainerId",
            "HeroHeadline", "HeroImageUrl", "HeroSubheadline", "LogoUrl", "MutedColor",
            "PageBackgroundColor", "PrivacyUrl", "PromoCode", "PromoDescription", "PromoPercent",
            "RequireAnalyticsConsent", "RequireCountry", "RequirePhone", "ResendFrom", "RespectDoNotTrack",
            "ScannerResultSeconds", "ShowCountdown", "StorefrontDescription", "StorefrontTitle", "SurfaceColor", "TermsUrl",
            "TextColor", "TicketPageSubtitle", "TicketPageTitle", "TimerColor", "TimerTextColor"
        };

        foreach (var field in persistedFields)
        {
            var controls = Regex.Matches(Settings,
                $"<(?:input|select|textarea)\\b[^>]*\\basp-for=\\\"{Regex.Escape(field)}\\\"");
            Assert.Single(controls.Cast<Match>());
        }

        Assert.Contains("method=\"post\"", Settings, StringComparison.Ordinal);
        Assert.Contains("enctype=\"multipart/form-data\"", Settings, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"SaveSettings\"", Settings, StringComparison.Ordinal);
        Assert.Contains("id=\"ticket-settings-form\"", Settings, StringComparison.Ordinal);

        Assert.Contains("name=\"resendApiKey\"", Settings, StringComparison.Ordinal);
        Assert.Contains("name=\"appleP12\"", Settings, StringComparison.Ordinal);
        Assert.Contains("name=\"appleP12Password\"", Settings, StringComparison.Ordinal);
        Assert.Contains("name=\"googleServiceAccountJson\"", Settings, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"Protected", Settings, StringComparison.Ordinal);

        Assert.Contains("id=\"open-theme-editor\"", Settings, StringComparison.Ordinal);
        Assert.Contains("id=\"close-theme-editor\"", Settings, StringComparison.Ordinal);
        Assert.Contains("id=\"save-theme-editor\"", Settings, StringComparison.Ordinal);
        Assert.Contains("id=\"theme-editor\"", Settings, StringComparison.Ordinal);
        Assert.Contains("id=\"theme-editor-frame\"", Settings, StringComparison.Ordinal);
        Assert.Contains("dialog.showModal()", Settings, StringComparison.Ordinal);
        Assert.Contains("form.requestSubmit()", Settings, StringComparison.Ordinal);

        var previewStates = Regex.Matches(Settings, "data-preview-state=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.NotEmpty(previewStates);
        Assert.Equal(previewStates.Length, previewStates.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Scanner_settings_explain_event_scope_and_support_manual_or_timed_close()
    {
        Assert.Contains("Event check-in scanner", Settings, StringComparison.Ordinal);
        Assert.Contains("Each event has its own protected public scanner link", Settings, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"ScannerResultSeconds\"", Settings, StringComparison.Ordinal);
        Assert.Contains("min=\"0\"", Settings, StringComparison.Ordinal);
        Assert.Contains("max=\"60\"", Settings, StringComparison.Ordinal);
        Assert.Contains("Set to 0", Settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_scanner_is_mobile_first_and_ready_for_repeated_door_check_ins()
    {
        Assert.Contains("Layout = null", Scanner, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", Scanner, StringComparison.Ordinal);
        Assert.Contains("viewport-fit=cover", Scanner, StringComparison.Ordinal);
        Assert.Contains("min-height:100svh", Scanner, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp:2", Scanner, StringComparison.Ordinal);
        Assert.Contains("<qrcode-stream", Scanner, StringComparison.Ordinal);
        Assert.Contains("v-on:decode=\"onDecode\"", Scanner, StringComparison.Ordinal);
        Assert.Contains("Camera unavailable", Scanner, StringComparison.Ordinal);
        Assert.Contains("Enter ticket code", Scanner, StringComparison.Ordinal);
        Assert.Contains("Gate / lane", Scanner, StringComparison.Ordinal);

        Assert.Contains("role=\"dialog\"", Scanner, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", Scanner, StringComparison.Ordinal);
        Assert.Contains("result.attendee", Scanner, StringComparison.Ordinal);
        Assert.Contains("result.ticketType", Scanner, StringComparison.Ordinal);
        Assert.Contains("Valid ticket", Scanner, StringComparison.Ordinal);
        Assert.Contains("Already checked in", Scanner, StringComparison.Ordinal);
        Assert.Contains("Wrong event", Scanner, StringComparison.Ordinal);
        Assert.Contains("Close · scan next", Scanner, StringComparison.Ordinal);

        Assert.Contains("autoCloseSeconds = Model.Settings.ScannerResultSeconds", Scanner, StringComparison.Ordinal);
        Assert.Contains("if(!this.fatal&&config.autoCloseSeconds>0)", Scanner, StringComparison.Ordinal);
        Assert.Contains("setTimeout(()=>this.closeResult(),config.autoCloseSeconds*1000)", Scanner,
            StringComparison.Ordinal);
        Assert.Contains("this.stopCamera();", Scanner, StringComparison.Ordinal);
        Assert.Contains("this.resumeCamera();", Scanner, StringComparison.Ordinal);
        Assert.Contains("this.cameraMounted=false", Scanner, StringComparison.Ordinal);
        Assert.Contains("setAttribute('inert','')", Scanner, StringComparison.Ordinal);
        Assert.Contains("addEventListener('pagehide',this.stopCamera)", Scanner, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('visibilitychange',this.onVisibilityChange)", Scanner,
            StringComparison.Ordinal);

        Assert.Contains("@Html.AntiForgeryToken()", Scanner, StringComparison.Ordinal);
        Assert.Contains("'RequestVerificationToken':token", Scanner, StringComparison.Ordinal);
        Assert.Contains("credentials:'same-origin'", Scanner, StringComparison.Ordinal);
        Assert.DoesNotContain("scannerToken", Scanner, StringComparison.Ordinal);
        Assert.DoesNotContain("_Analytics", Scanner, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_scanner_exchanges_the_capability_for_a_path_scoped_session_and_secures_check_in()
    {
        var scannerActions = PublicController[
            PublicController.IndexOf("public async Task<IActionResult> Scanner", StringComparison.Ordinal)..
            PublicController.IndexOf("private async Task<IActionResult> ShowEvent", StringComparison.Ordinal)];

        Assert.Contains("[HttpGet(\"{eventId}/scanner\")]", PublicController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"{eventId}/scanner/check-in\")]", PublicController, StringComparison.Ordinal);
        Assert.True(Regex.Matches(PublicController, "\\[TicketNoStore\\]").Count >= 2);
        Assert.Contains("TicketCodeService.CanAccessScanner(item, scannerToken)", scannerActions,
            StringComparison.Ordinal);
        Assert.Contains("Response.Cookies.Append(TicketCodeService.ScannerSessionCookieName", PublicController,
            StringComparison.Ordinal);
        Assert.Contains("HttpOnly = true", PublicController, StringComparison.Ordinal);
        Assert.Contains("SameSite = SameSiteMode.Strict", PublicController, StringComparison.Ordinal);
        Assert.Contains("SetScannerSessionCookie(storeId, item.Slug", scannerActions, StringComparison.Ordinal);
        Assert.Contains("Path = path", PublicController, StringComparison.Ordinal);
        Assert.Contains("return RedirectPublic(nameof(Scanner)", scannerActions, StringComparison.Ordinal);
        Assert.Contains("Request.Cookies[TicketCodeService.ScannerSessionCookieName]", scannerActions,
            StringComparison.Ordinal);
        Assert.Contains("[ValidateAntiForgeryToken]", PublicController, StringComparison.Ordinal);
        Assert.Contains("repository.CheckIn(storeId, item", scannerActions, StringComparison.Ordinal);
        Assert.DoesNotContain("TicketEventSalePolicy.CanStartCheckout", scannerActions, StringComparison.Ordinal);

        Assert.Contains("Referrer-Policy", PublicController, StringComparison.Ordinal);
        Assert.Contains("noindex, nofollow, noarchive", PublicController, StringComparison.Ordinal);
        Assert.Contains("X-Frame-Options", PublicController, StringComparison.Ordinal);
        Assert.Contains("Permissions-Policy", PublicController, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", TicketCodes, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_summaries_render_only_when_errors_exist()
    {
        Assert.Contains("ViewData.ModelState.ErrorCount > 0", Settings, StringComparison.Ordinal);
        Assert.Contains("asp-validation-summary=\"All\"", Settings, StringComparison.Ordinal);
        Assert.Contains("ViewData.ModelState.ErrorCount > 0", EventEditor, StringComparison.Ordinal);
        Assert.Contains("asp-validation-summary=\"All\"", EventEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void MakePay_attribution_is_enforced_and_not_merchant_editable()
    {
        Assert.DoesNotContain("asp-for=\"ShowMakePayPromotion\"", Settings, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"PromotionText\"", Settings, StringComparison.Ordinal);

        Assert.Contains("EnforceMakePayAttribution", Repository, StringComparison.Ordinal);
        Assert.Contains("EnforcedPromotionText", Repository, StringComparison.Ordinal);
        var readPath = Repository[Repository.IndexOf("GetSettings", StringComparison.Ordinal)..Repository.IndexOf("SaveSettings", StringComparison.Ordinal)];
        Assert.DoesNotContain("UpdateSetting", readPath, StringComparison.Ordinal);

        Assert.DoesNotContain("ShowMakePayPromotion", Footer, StringComparison.Ordinal);
        Assert.DoesNotContain("Model.PromotionText", Footer, StringComparison.Ordinal);
        Assert.Contains("EnforcedPromotionText", Footer, StringComparison.Ordinal);
        Assert.Contains("https://makepay.io", Footer, StringComparison.Ordinal);
        Assert.Contains("https://btcpayserver.org", Footer, StringComparison.Ordinal);
        Assert.Contains("✦ MakePay", Footer, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.CleanController", Footer, StringComparison.Ordinal);
        Assert.Contains("cleanUrls ? null", Footer, StringComparison.Ordinal);
        Assert.Contains("src=\"~/img/btcpay-logo-white-txt.svg\"", Footer, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelled_orders_recover_to_a_saleable_event_or_the_storefront()
    {
        Assert.Contains("!TicketEventSalePolicy.CanStartCheckout(item, DateTimeOffset.UtcNow)", PublicController,
            StringComparison.Ordinal);
        Assert.Contains("return RedirectPublic(nameof(Storefront), new { storeId });", PublicController,
            StringComparison.Ordinal);
        Assert.Contains("var eventOnSale = TicketEventSalePolicy.CanStartCheckout", Order, StringComparison.Ordinal);
        foreach (var recoveryView in new[] { Cart, Details, Order })
        {
            Assert.Contains("eventOnSale", recoveryView, StringComparison.Ordinal);
            Assert.Contains("@if (eventOnSale)", recoveryView, StringComparison.Ordinal);
            Assert.Contains("asp-action=\"Storefront\"", recoveryView, StringComparison.Ordinal);
        }
        Assert.Contains("this event is no longer on sale", Order, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Model.Event.EndsAt > Model.Order.ReservationExpiresAt", Countdown,
            StringComparison.Ordinal);
        Assert.Contains("Url.Action(\"Storefront\"", Countdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoice_backed_orders_never_render_or_redirect_as_expired()
    {
        Assert.Contains("TicketReservationPolicy.CanExpire(Model.Order, DateTimeOffset.UtcNow)", Details,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Model.Order.ReservationExpiresAt <= DateTimeOffset.UtcNow", Details,
            StringComparison.Ordinal);

        Assert.Contains("Model.Order.InvoiceId", Countdown, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace", Countdown, StringComparison.Ordinal);
        Assert.Contains("data-et-expired-url", Countdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_order_page_polls_the_sanitized_status_endpoint()
    {
        Assert.Contains("Url.Action(\"OrderStatus\"", Order, StringComparison.Ordinal);
        Assert.Contains("fetch(statusUrl", Order, StringComparison.Ordinal);
        Assert.Contains("cache:'no-store'", Order, StringComparison.Ordinal);
        Assert.Contains("setTimeout(poll", Order, StringComparison.Ordinal);
        Assert.Contains("result.status==='paid'", Order, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize(Model.Order.InvoiceId)", Order, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_public_experience_uses_one_shared_branded_footer()
    {
        foreach (var view in new[] { "Storefront", "Event", "Cart", "Details", "Payment", "Order" })
        {
            var source = Source("Views", "EventTickets", "Public", $"{view}.cshtml");
            Assert.Contains("~/Views/EventTickets/Public/_Footer.cshtml", source, StringComparison.Ordinal);
        }

        Assert.Contains("Powered by BTCPay", Footer, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", Footer, StringComparison.Ordinal);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(RepositoryFile(["src", "BTCPayServer.Plugins.MakePay.EventTickets", .. segments]));

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }
}
