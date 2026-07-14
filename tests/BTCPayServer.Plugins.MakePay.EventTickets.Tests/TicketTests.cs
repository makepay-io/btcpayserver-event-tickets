using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using Microsoft.AspNetCore.DataProtection;
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
    public void CustomDomainGuideSeparatesPluginRoutesFromServerOwnedDnsAndTls()
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
        Assert.Contains("DNS alone does not remove the store ID", settings, StringComparison.Ordinal);
        Assert.Contains("not registered as a BTCPay App", settings, StringComparison.Ordinal);
        Assert.Contains("Let's Encrypt", settings, StringComparison.Ordinal);
        Assert.Contains("aliases the whole BTCPay Server, not only this store", settings, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", settings, StringComparison.Ordinal);
        Assert.Contains("docs.btcpayserver.org/FAQ/Apps/", settings, StringComparison.Ordinal);
        Assert.Contains("docs.btcpayserver.org/FAQ/Deployment/", settings, StringComparison.Ordinal);
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
