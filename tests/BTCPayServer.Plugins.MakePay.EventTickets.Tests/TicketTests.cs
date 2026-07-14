using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Tests;

public class TicketTests
{
    [Fact]
    public void QrPayloadRoundTripsCode()
    {
        var code = "TKT-AAAA-BBBB-CCCC-DDDD";
        Assert.Equal(code, TicketCodeService.ExtractCode(TicketCodeService.QrPayload("store", code)));
    }

    [Fact]
    public void CodeHashNormalizesCaseAndSpaces() => Assert.Equal(TicketCodeService.Hash(" tkt-ab "), TicketCodeService.Hash("TKT-AB"));

    [Fact]
    public void ForeignQrPrefixFallsBackToRawValue() => Assert.Equal("hello", TicketCodeService.ExtractCode("hello"));

    [Fact]
    public void SelectionBuildsMultipleImmutablePriceLines()
    {
        var item = Event();
        var lines = TicketCheckoutService.BuildLines(item, new Dictionary<string, int> { ["general"] = 2, ["vip"] = 1 });
        Assert.Collection(lines,
            line => { Assert.Equal("general", line.TicketTypeId); Assert.Equal(2, line.Quantity); Assert.Equal(25m, line.UnitPrice); },
            line => { Assert.Equal("vip", line.TicketTypeId); Assert.Equal(1, line.Quantity); Assert.Equal(75m, line.UnitPrice); });
    }

    [Fact]
    public void SelectionRejectsQuantityBeyondPerOrderLimit()
    {
        var lines = TicketCheckoutService.BuildLines(Event(), new Dictionary<string, int> { ["general"] = 9 });
        Assert.Empty(lines);
    }

    [Fact]
    public void RebuyKeepsQuantitiesAndUsesCurrentPrices()
    {
        var item = Event();
        item.TicketTypes[0].Price = 30m;
        var previous = new TicketOrder
        {
            Lines =
            [
                new TicketOrderLine { TicketTypeId = "general", Quantity = 2, UnitPrice = 25m },
                new TicketOrderLine { TicketTypeId = "vip", Quantity = 1, UnitPrice = 75m }
            ]
        };

        var lines = TicketCheckoutService.BuildRebuyLines(previous, item);

        Assert.Collection(lines,
            line => { Assert.Equal("general", line.TicketTypeId); Assert.Equal(2, line.Quantity); Assert.Equal(30m, line.UnitPrice); },
            line => { Assert.Equal("vip", line.TicketTypeId); Assert.Equal(1, line.Quantity); Assert.Equal(75m, line.UnitPrice); });
    }

    [Fact]
    public void RebuyRejectsUnavailablePreviousTicketType()
    {
        var item = Event();
        item.TicketTypes[0].Active = false;
        var previous = new TicketOrder
        {
            Lines = [new TicketOrderLine { TicketTypeId = "general", Quantity = 2, UnitPrice = 25m }]
        };

        Assert.Empty(TicketCheckoutService.BuildRebuyLines(previous, item));
    }

    [Fact]
    public void PromotionRecalculatesOrderTotal()
    {
        var order = new TicketOrder { Lines = [new TicketOrderLine { TicketTypeId = "general", Quantity = 2, UnitPrice = 25m }] };
        TicketCheckoutService.Recalculate(order);
        var applied = TicketCheckoutService.ApplyPromo(order, new EventTicketSettings { PromoCode = "BUILD", PromoPercent = 20 }, "build");
        Assert.True(applied);
        Assert.Equal(50m, order.Subtotal);
        Assert.Equal(10m, order.DiscountAmount);
        Assert.Equal(40m, order.Total);
    }

    [Fact]
    public void LegacyOrderResolvesToCurrentTicketType()
    {
        var lines = TicketCheckoutService.ResolveLines(new TicketOrder { TicketTypeId = "vip", Quantity = 2 }, Event());
        var line = Assert.Single(lines);
        Assert.Equal(75m, line.UnitPrice);
        Assert.Equal(2, line.Quantity);
    }

    [Fact]
    public void PublicOrderTokenIsEncryptedAndComparedSafely()
    {
        var service = Checkout(out _);
        var order = new TicketOrder();
        var token = service.CreateAccessToken(order);
        Assert.True(service.CanAccess(order, token));
        Assert.False(service.CanAccess(order, token + "tampered"));
        Assert.DoesNotContain(token, order.ProtectedPublicAccessToken!);
    }

    [Fact]
    public void PdfContainsQrImageAndOnePagePerTicket()
    {
        var checkout = Checkout(out var codes);
        _ = checkout;
        var created = codes.Create();
        var ticket = new IssuedTicket { StoreId = "store", EventId = "event", TicketTypeId = "general", AttendeeName = "Ada Builder", ProtectedCode = created.Protected, CodeHash = created.Hash };
        var bytes = new TicketDocumentService(codes).CreatePdf(Event(), new TicketOrder { Id = "order", StoreId = "store", BuyerName = "Ada Builder" }, [ticket]);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Subtype /Image", text);
        Assert.Contains("/Count 1", text);
    }

    [Fact]
    public void ExpiredReservationWithoutInvoiceCanBeReleased()
    {
        var now = DateTimeOffset.UtcNow;
        var order = new TicketOrder { Status = TicketOrderStatus.Pending, ReservationExpiresAt = now.AddSeconds(-1) };
        Assert.True(TicketReservationPolicy.CanExpire(order, now));
        Assert.False(TicketReservationPolicy.HoldsInventory(order, now));
    }

    [Fact]
    public void InvoiceProtectsReservationAfterCheckoutTimerEnds()
    {
        var now = DateTimeOffset.UtcNow;
        var order = new TicketOrder { Status = TicketOrderStatus.Pending, InvoiceId = "invoice-1", ReservationExpiresAt = now.AddMinutes(-10) };
        Assert.False(TicketReservationPolicy.CanExpire(order, now));
        Assert.True(TicketReservationPolicy.HoldsInventory(order, now));
    }

    [Fact]
    public void AnalyticsIdentifiersAcceptLowercasePasteAndRejectMalformedIds()
    {
        var valid = new EventTicketSettings
        {
            GoogleTagManagerContainerId = "  gtm-abc123  ",
            GoogleAnalyticsMeasurementId = " g-abc123def4 "
        };
        var errors = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(valid, new ValidationContext(valid), errors, true));

        var invalid = new EventTicketSettings { GoogleTagManagerContainerId = "GTM-<script>" };
        errors.Clear();
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), errors, true));
        Assert.Contains(errors, result => result.MemberNames.Contains(nameof(EventTicketSettings.GoogleTagManagerContainerId)));
    }

    [Fact]
    public void FaviconUrlNormalizesWhitespaceAndRejectsNonHttpSchemes()
    {
        var settings = new EventTicketSettings { FaviconUrl = "  https://cdn.example.com/event-icon.png  " };
        Assert.Equal("https://cdn.example.com/event-icon.png", settings.FaviconUrl);

        var errors = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(settings, new ValidationContext(settings), errors, true));

        foreach (var unsafeUrl in new[] { "javascript:alert(1)", "ftp://cdn.example.com/icon.ico", "data:image/svg+xml,<svg/>" })
        {
            settings.FaviconUrl = unsafeUrl;
            errors.Clear();
            Assert.False(Validator.TryValidateObject(settings, new ValidationContext(settings), errors, true));
            Assert.Contains(errors, result => result.MemberNames.Contains(nameof(EventTicketSettings.FaviconUrl)));
        }

        settings.FaviconUrl = "   ";
        Assert.Null(settings.FaviconUrl);
        errors.Clear();
        Assert.True(Validator.TryValidateObject(settings, new ValidationContext(settings), errors, true));
    }

    [Fact]
    public void EveryPublicPageUsesSharedConditionalFaviconHead()
    {
        var theme = File.ReadAllText(RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.EventTickets",
            "Views",
            "EventTickets",
            "Public",
            "_Theme.cshtml"));
        Assert.Contains("Uri.TryCreate(Model.FaviconUrl", theme, StringComparison.Ordinal);
        Assert.Contains("faviconUri.Scheme == Uri.UriSchemeHttp", theme, StringComparison.Ordinal);
        Assert.Contains("faviconUri.Scheme == Uri.UriSchemeHttps", theme, StringComparison.Ordinal);
        Assert.Contains("if (faviconUrl is not null)", theme, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"icon\" href=\"@faviconUrl\">", theme, StringComparison.Ordinal);

        foreach (var view in new[] { "Storefront.cshtml", "Event.cshtml", "Cart.cshtml", "Details.cshtml", "Payment.cshtml", "Order.cshtml" })
        {
            var source = File.ReadAllText(RepositoryFile(
                "src",
                "BTCPayServer.Plugins.MakePay.EventTickets",
                "Views",
                "EventTickets",
                "Public",
                view));
            Assert.Contains("~/Views/EventTickets/Public/_Theme.cshtml", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StoreNavigationUsesBundledCurrentColorQrCodeIcon()
    {
        var navigation = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Views", "Shared", "EventTickets", "StoreNavExtension.cshtml"));
        var sprite = File.ReadAllText(RepositoryFile(
            "submodules", "btcpayserver", "BTCPayServer", "wwwroot", "img", "icon-sprite.svg"));

        Assert.Contains("<vc:icon symbol=\"qr-code\" />", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", navigation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", navigation, StringComparison.OrdinalIgnoreCase);

        var symbolStart = sprite.IndexOf("<symbol id=\"qr-code\"", StringComparison.Ordinal);
        Assert.True(symbolStart >= 0, "BTCPay's bundled icon sprite must contain the QR code symbol.");
        var symbolEnd = sprite.IndexOf("</symbol>", symbolStart, StringComparison.Ordinal);
        Assert.True(symbolEnd > symbolStart, "The bundled QR code symbol must be complete.");
        var qrCodeSymbol = sprite[symbolStart..(symbolEnd + "</symbol>".Length)];
        Assert.Contains("currentColor", qrCodeSymbol, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyticsItemsMapPublicTicketVariantWithoutCustomerData()
    {
        var item = Event();
        var order = new TicketOrder
        {
            Id = "order-public-1",
            Currency = "usd",
            Subtotal = 50m,
            DiscountAmount = 10m,
            Total = 40m,
            PromoCode = "BUILD",
            BuyerEmail = "private@example.com",
            BuyerName = "Private Buyer",
            InvoiceId = "invoice-secret",
            ProtectedPublicAccessToken = "protected-secret"
        };
        var lines = new[]
        {
            new TicketLineViewModel
            {
                TicketType = item.TicketTypes[0],
                Line = new TicketOrderLine { TicketTypeId = "general", Quantity = 2, UnitPrice = 25m }
            }
        };

        var payload = TicketAnalytics.ForOrder(order, item, lines, purchase: true);
        var json = JsonSerializer.Serialize(payload);

        Assert.Equal("USD", payload.Currency);
        Assert.StartsWith("mpt_", payload.TransactionId);
        Assert.Equal(68, payload.TransactionId!.Length);
        Assert.DoesNotContain(order.Id, payload.TransactionId, StringComparison.Ordinal);
        Assert.Equal(40m, payload.Value);
        Assert.Equal("BUILD", payload.Coupon);
        var analyticsItem = Assert.Single(payload.Items);
        Assert.Equal("general", analyticsItem.TicketTypeId);
        Assert.Equal("General", analyticsItem.ItemVariant);
        Assert.Equal(2, analyticsItem.Quantity);
        Assert.Equal(20m, analyticsItem.Price);
        Assert.Equal(5m, analyticsItem.Discount);
        Assert.Equal(payload.Value, payload.Items.Sum(item => item.Price * item.Quantity));
        Assert.Contains("\"ticket_type_id\":\"general\"", json);
        Assert.DoesNotContain("private@example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private Buyer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoice-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protected-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(order.Id, json, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseAfterConsentUsesOneSharedContractEvent() =>
        Assert.Equal("makepay:analytics-consent-granted", TicketAnalytics.ConsentGrantedEvent);

    [Fact]
    public void CheckoutViewsUseOnlyHashedIdentifiersForAnalyticsDedupeState()
    {
        var views = new[] { "Cart.cshtml", "Details.cshtml", "Payment.cshtml", "Order.cshtml" };
        foreach (var view in views)
        {
            var source = File.ReadAllText(RepositoryFile(
                "src",
                "BTCPayServer.Plugins.MakePay.EventTickets",
                "Views",
                "EventTickets",
                "Public",
                view));
            var analyticsCalls = source
                .Split('\n')
                .Where(line => line.Contains("trackOnce(", StringComparison.Ordinal) ||
                               line.Contains("purchaseOnce(", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(analyticsCalls);
            Assert.Contains("TicketAnalytics.AnalyticsTransactionId(Model.Order)", source, StringComparison.Ordinal);
            Assert.All(analyticsCalls, line => Assert.Contains("analyticsOrderId", line, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AnalyticsConsentRevocationReloadsAnAlreadyStartedGoogleProvider()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.EventTickets",
            "Views",
            "EventTickets",
            "Public",
            "_Analytics.cshtml"));

        Assert.Contains("const reloadWithoutProvider=externalStarted", source, StringComparison.Ordinal);
        Assert.Contains("if(reloadWithoutProvider)location.reload()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GoogleAnalyticsCspContractIsNarrowAndComplete()
    {
        Assert.Equal(["https://*.googletagmanager.com"], TicketAnalytics.GoogleScriptSources);
        Assert.Equal(
            ["'self'", "https://*.google-analytics.com", "https://*.analytics.google.com", "https://*.googletagmanager.com"],
            TicketAnalytics.GoogleConnectSources);
        Assert.DoesNotContain("https:", TicketAnalytics.GoogleScriptSources);
        Assert.DoesNotContain("*", TicketAnalytics.GoogleConnectSources);
    }

    [Fact]
    public void CustomDomainGuideUsesExplicitNativeAppMappingWithoutHiddenWrites()
    {
        var settings = File.ReadAllText(RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.EventTickets",
            "Views",
            "EventTickets",
            "Settings.cshtml"));

        Assert.Contains("Use your own domain", settings, StringComparison.Ordinal);
        Assert.Contains("Server administrator required", settings, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.ToAbsoluteHttpUrl", settings, StringComparison.Ordinal);
        Assert.Contains("BTCPAY_ADDITIONAL_HOSTS", settings, StringComparison.Ordinal);
        Assert.Contains("Create Event Tickets App", settings, StringComparison.Ordinal);
        Assert.Contains("Server Settings → Policies", settings, StringComparison.Ordinal);
        Assert.Contains("Merely opening or saving this plugin never creates an App record", settings, StringComparison.Ordinal);
        Assert.Contains("Native clean routes are available", settings, StringComparison.Ordinal);
        Assert.Contains("GET/HEAD requests move to the clean hostname", settings, StringComparison.Ordinal);
        Assert.Contains("POST requests are never cross-host redirected", settings, StringComparison.Ordinal);
        Assert.Contains("Let's Encrypt", settings, StringComparison.Ordinal);
        Assert.Contains("aliases the whole BTCPay Server, not only this store", settings, StringComparison.Ordinal);
        Assert.Contains("canonical ASCII hostname", settings, StringComparison.Ordinal);
        Assert.Contains("first exact hostname row", settings, StringComparison.Ordinal);
        Assert.Contains("Domain mapping inactive or ambiguous", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("BTCPay rejects duplicate domain mappings", settings, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", settings, StringComparison.Ordinal);
        Assert.Contains("docs.btcpayserver.org/FAQ/Apps/", settings, StringComparison.Ordinal);
        Assert.Contains("docs.btcpayserver.org/FAQ/Deployment/", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEventTicketsAppRegistrationIsExplicitAndReadOnlyOnPluginGets()
    {
        var plugin = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "EventTicketsPlugin.cs"));
        var appType = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Services", "EventTicketsAppType.cs"));
        var appService = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Services", "EventTicketsAppService.cs"));
        var admin = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Controllers", "EventTicketsAdminController.cs"));

        Assert.Contains("AddSingleton<AppBaseType, EventTicketsAppType>", plugin, StringComparison.Ordinal);
        Assert.Contains("Description = \"MakePay Event Tickets", appType, StringComparison.Ordinal);
        Assert.Contains("EventTicketsAdminController.Settings", appType, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.LegacyController", appType, StringComparison.Ordinal);
        Assert.Contains("GetPathByAction", appType, StringComparison.Ordinal);
        Assert.Contains("GetApps(EventTicketsAppType.AppType)", appService, StringComparison.Ordinal);
        Assert.Contains("byId.TryGetValue(mapping.AppId", appService, StringComparison.Ordinal);
        Assert.Contains("mapping.AppType != EventTicketsAppType.AppType", appService, StringComparison.Ordinal);
        Assert.Contains("includeStore: true", appService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreate", appService, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOrCreateApp", appService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreate", admin, StringComparison.Ordinal);
        Assert.Contains("apps/create/{EventTicketsAppType.AppType}", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void BothLegacyAndMappedControllersExposeEveryPublicTicketAction()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Controllers", "EventTicketsPublicController.cs"));
        var expected = new[]
        {
            "[HttpGet(\"\")]",
            "[HttpGet(\"{eventId}\")]",
            "[HttpGet(\"{eventId}/pos\")]",
            "[HttpPost(\"{eventId}/checkout\")]",
            "[HttpPost(\"{eventId}/checkout/{orderId}/rebuy\")]",
            "[HttpGet(\"{eventId}/checkout/{orderId}/cart\")]",
            "[HttpPost(\"{eventId}/checkout/{orderId}/promotion\")]",
            "[HttpGet(\"{eventId}/checkout/{orderId}/details\")]",
            "[HttpPost(\"{eventId}/checkout/{orderId}/payment\")]",
            "[HttpGet(\"{eventId}/checkout/{orderId}/payment\")]",
            "[HttpGet(\"order/{orderId}/status\")]",
            "[HttpGet(\"order/{orderId}\")]",
            "[HttpGet(\"order/{orderId}/tickets.pdf\")]",
            "[HttpGet(\"order/{orderId}/wallet/apple/{ticketId}\")]"
        };
        Assert.All(expected, route => Assert.Contains(route, source, StringComparison.Ordinal));
        Assert.Contains("[Route(\"stores/{storeId}/events\")]", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"events\")]", source, StringComparison.Ordinal);
        Assert.Contains("[DomainMappingConstraint(EventTicketsAppType.AppType)]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/\")]", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class EventTicketsPublicController", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class CleanEventTicketsPublicController", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanControllerDerivesTenantOnlyFromMappedAppData()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Controllers", "EventTicketsPublicController.cs"));

        Assert.Contains("RouteData.Values[\"appId\"]", source, StringComparison.Ordinal);
        Assert.Contains("await EventApps.Get(appId)", source, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.BindMappedStore(app.StoreDataId", source, StringComparison.Ordinal);
        Assert.Contains("HttpContext.SetAppData(app)", source, StringComparison.Ordinal);
        Assert.Contains("HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method)", source, StringComparison.Ordinal);
        Assert.Contains("!Request.IsOnion()", source, StringComparison.Ordinal);
        Assert.Contains("preserveMethod: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[Route(\"events/{storeId}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicViewsSwitchAllTicketLinksAndFormsToTheCleanControllerSurface()
    {
        var directory = Path.GetDirectoryName(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Views", "EventTickets", "Public", "Storefront.cshtml"))!;
        var source = string.Join('\n', Directory.GetFiles(directory, "*.cshtml").Select(File.ReadAllText));

        Assert.Contains("Model.CleanUrls ? null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-controller=\"EventTicketsPublic\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/stores/@Model", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"StartCheckout\"", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ApplyPromotion\"", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"CreatePayment\"", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Rebuy\"", source, StringComparison.Ordinal);
        Assert.Contains("Url.Action(\"OrderStatus\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentAndInvoiceCallbacksUseMappedCanonicalOrderUrls()
    {
        var fulfillment = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Services", "EventTicketFulfillmentService.cs"));
        var publicController = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Controllers", "EventTicketsPublicController.cs"));

        Assert.Contains("await eventApps.GetMappedBaseUrl(order.StoreId)", fulfillment, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.OrderUrl", fulfillment, StringComparison.Ordinal);
        Assert.Contains("TicketPublicUrl.OrderUrl(order, accessToken, await EventApps.GetMappedBaseUrl(storeId))",
            publicController, StringComparison.Ordinal);
        Assert.Contains("RedirectURL = successUrl", publicController, StringComparison.Ordinal);
        Assert.Contains("OrderUrl = successUrl", publicController, StringComparison.Ordinal);
        Assert.Contains("PublicActionLink(nameof(Pdf)", publicController, StringComparison.Ordinal);
        Assert.Contains("PublicActionLink(nameof(AppleWallet)", publicController, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("/", "")]
    [InlineData("btcpay", "/btcpay")]
    [InlineData("/btcpay/", "/btcpay")]
    public void RootPathNormalizationIsStable(string? value, string expected) =>
        Assert.Equal(expected, TicketPublicUrl.NormalizeRootPath(value));

    [Theory]
    [InlineData("TICKETS.Example.COM.", "tickets.example.com")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void NativeMappingHostNormalizationIsExact(string value, string expected)
    {
        Assert.True(TicketPublicUrl.TryNormalizeHost(value, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://tickets.example.com")]
    [InlineData("tickets.example.com:443")]
    [InlineData("*.example.com")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("singlelabel")]
    [InlineData("bad_name.example")]
    public void NativeMappingRejectsUnsafeOrAmbiguousHosts(string value) =>
        Assert.False(TicketPublicUrl.TryNormalizeHost(value, out _, out _));

    [Theory]
    [InlineData("tickets.example.com", true, "tickets.example.com")]
    [InlineData("TICKETS.EXAMPLE.COM", true, "tickets.example.com")]
    [InlineData("tickets.example.com.", false, "")]
    [InlineData(" tickets.example.com", false, "")]
    [InlineData("bücher.example", false, "")]
    [InlineData("xn--bcher-kva.example", true, "xn--bcher-kva.example")]
    public void NativeMappingOnlyActivatesRawCanonicalAsciiHostnames(string value, bool expected, string normalized)
    {
        Assert.Equal(expected, TicketPublicUrl.TryGetNativeMappedHost(value, out var actual));
        Assert.Equal(normalized, actual);
    }

    [Fact]
    public void NativeMappingFollowsFirstExactGlobalDomainRow()
    {
        var mappings = new[]
        {
            new TicketPublicUrl.NativeAppMapping("tickets.example.com", "other-app", "PointOfSale"),
            new TicketPublicUrl.NativeAppMapping("TICKETS.EXAMPLE.COM", "event-app", "MakePayEventTickets")
        };

        Assert.False(TicketPublicUrl.TryGetEffectiveNativeMappedHost(
            mappings[1].Domain, "event-app", "MakePayEventTickets", mappings, out _));
        Assert.True(TicketPublicUrl.TryGetEffectiveNativeMappedHost(
            mappings[0].Domain, "other-app", "PointOfSale", mappings, out var domain));
        Assert.Equal("tickets.example.com", domain);
        Assert.False(TicketPublicUrl.TryGetEffectiveNativeMappedHost(
            mappings[0].Domain, "OTHER-APP", "PointOfSale", mappings, out _));
    }

    [Fact]
    public void CleanMappedPostReplacesSyntheticStoreBindingError()
    {
        var actionArguments = new Dictionary<string, object?>
        {
            ["storeId"] = null,
            ["input"] = new TicketCheckoutInput()
        };
        var routeValues = new RouteValueDictionary();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("storeId", "The storeId field is required.");

        TicketPublicUrl.BindMappedStore("mapped-store", actionArguments, routeValues, modelState);

        Assert.True(modelState.IsValid);
        Assert.Equal("mapped-store", actionArguments["storeId"]);
        Assert.Equal("mapped-store", routeValues["storeId"]);
        Assert.True(actionArguments.ContainsKey("input"));
    }

    [Fact]
    public void CleanMappedRootDoesNotInventAStoreActionArgument()
    {
        var actionArguments = new Dictionary<string, object?>();
        var routeValues = new RouteValueDictionary();
        var modelState = new ModelStateDictionary();

        TicketPublicUrl.BindMappedStore("mapped-store", actionArguments, routeValues, modelState);

        Assert.Empty(actionArguments);
        Assert.Equal("mapped-store", routeValues["storeId"]);
        Assert.True(modelState.IsValid);
    }

    [Fact]
    public void PublicUrlBuilderGeneratesCleanAndLegacyOrderLinks()
    {
        var order = new TicketOrder
        {
            Id = "order-1",
            StoreId = "store-1",
            PublicBaseUrl = "https://pay.example.com/btcpay"
        };

        Assert.Equal("/events/summit", TicketPublicUrl.EventPath(true, order.StoreId, "summit"));
        Assert.Equal("/stores/store-1/events/summit", TicketPublicUrl.EventPath(false, order.StoreId, "summit"));
        Assert.Equal("https://tickets.example.com/btcpay/events/order/order-1?accessToken=secret%20value",
            TicketPublicUrl.OrderUrl(order, "secret value", "https://tickets.example.com/btcpay"));
        Assert.Equal("https://pay.example.com/btcpay/stores/store-1/events/order/order-1?accessToken=secret%20value",
            TicketPublicUrl.OrderUrl(order, "secret value", null));
    }

    [Fact]
    public void OnionCheckoutPreservesItsOriginEvenWhenStoreHasClearnetMapping()
    {
        var order = new TicketOrder { Id = "order-1", StoreId = "store-1" };

        TicketPublicUrl.CaptureOrderOrigin(
            order,
            cleanUrls: false,
            isOnion: true,
            mappedBaseUrl: "https://tickets.example.com/btcpay",
            requestBaseUrl: "http://tickets-example.onion/btcpay");

        Assert.False(order.PreferCleanUrls);
        Assert.Equal("http://tickets-example.onion/btcpay", order.PublicBaseUrl);
        Assert.Equal(
            "http://tickets-example.onion/btcpay/stores/store-1/events/order/order-1?accessToken=secret",
            TicketPublicUrl.OrderUrl(order, "secret", "https://tickets.example.com/btcpay"));
    }

    [Theory]
    [InlineData(false, false, "https://tickets.example.com", true)]
    [InlineData(true, false, "https://tickets.example.com", true)]
    [InlineData(false, false, null, false)]
    [InlineData(true, true, "https://tickets.example.com", false)]
    public void NewOrdersPersistExplicitRouteOriginIntent(
        bool cleanUrls,
        bool isOnion,
        string? mappedBaseUrl,
        bool expected)
    {
        var order = new TicketOrder();
        TicketPublicUrl.CaptureOrderOrigin(order, cleanUrls, isOnion, mappedBaseUrl, "https://pay.example.com");
        Assert.Equal(expected, order.PreferCleanUrls);
    }

    [Fact]
    public void OrdersPersistedBeforeOriginIntentKeepHistoricalMappingBehavior()
    {
        var order = JsonSerializer.Deserialize<TicketOrder>(
            "{\"Id\":\"old-order\",\"StoreId\":\"store-1\",\"PublicBaseUrl\":\"https://pay.example.com/btcpay\"}")!;

        Assert.Null(order.PreferCleanUrls);
        Assert.Equal("https://tickets.example.com/btcpay/events/order/old-order",
            TicketPublicUrl.OrderUrl(order, null, "https://tickets.example.com/btcpay"));
        Assert.Equal("https://pay.example.com/btcpay/stores/store-1/events/order/old-order",
            TicketPublicUrl.OrderUrl(order, null, null));
    }

    [Fact]
    public void OnionOrdersPersistedBeforeOriginIntentNeverUpgradeToClearnetLinks()
    {
        var order = JsonSerializer.Deserialize<TicketOrder>(
            "{\"Id\":\"old-onion-order\",\"StoreId\":\"store-1\",\"PublicBaseUrl\":\"http://tickets-example.onion/btcpay\"}")!;

        Assert.Null(order.PreferCleanUrls);
        Assert.True(TicketPublicUrl.IsOnionBaseUrl(order.PublicBaseUrl));
        Assert.Equal(
            "http://tickets-example.onion/btcpay/stores/store-1/events/order/old-onion-order?accessToken=secret",
            TicketPublicUrl.OrderUrl(order, "secret", "https://tickets.example.com/btcpay"));
    }

    [Fact]
    public void LegacyCanonicalRedirectPreservesRootPathSuffixAndQueryExactlyOnce()
    {
        var result = TicketPublicUrl.CleanUrlFromLegacy(
            "https://tickets.example.com/btcpay",
            "store-1",
            new PathString("/btcpay"),
            new PathString("/stores/store-1/events/summit"),
            new QueryString("?campaign=launch"));

        Assert.Equal("https://tickets.example.com/btcpay/events/summit?campaign=launch", result);
        Assert.Null(TicketPublicUrl.CleanUrlFromLegacy(
            "https://tickets.example.com/btcpay",
            "store-2",
            new PathString("/btcpay"),
            new PathString("/stores/store-1/events/summit"),
            QueryString.Empty));

        var controller = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Controllers", "EventTicketsPublicController.cs"));
        Assert.Contains("Request.PathBase.Add(new PathString(\"/events\"))", controller, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/stores/store-1/events/summit", "https", "pay.example.com", "https://pay.example.com/stores/store-1/events/summit")]
    [InlineData("stores/store-1/events/summit", "https", "pay.example.com:8443", "https://pay.example.com:8443/stores/store-1/events/summit")]
    [InlineData("https://tickets.example.com/stores/store-1/events/summit", "https", "pay.example.com", "https://tickets.example.com/stores/store-1/events/summit")]
    [InlineData("http://tickets.example.com/stores/store-1/events/summit", "https", "pay.example.com", "http://tickets.example.com/stores/store-1/events/summit")]
    public void TicketPublicUrlKeepsHttpUrlsAndAnchorsRelativeRoutesToRequestHost(
        string pathOrUrl,
        string requestScheme,
        string requestHost,
        string expected)
    {
        Assert.Equal(expected, TicketPublicUrl.ToAbsoluteHttpUrl(pathOrUrl, requestScheme, requestHost));
    }

    private static TicketCheckoutService Checkout(out TicketCodeService codes)
    {
        codes = new TicketCodeService(new EphemeralDataProtectionProvider());
        return new TicketCheckoutService(codes);
    }

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }

    private static TicketEvent Event() => new()
    {
        Id = "event",
        Slug = "event",
        Name = "Builder Summit",
        TicketTypes =
        [
            new TicketType { Id = "general", Name = "General", Price = 25m, MaxPerOrder = 8 },
            new TicketType { Id = "vip", Name = "VIP", Price = 75m, MaxPerOrder = 4 }
        ]
    };
}
