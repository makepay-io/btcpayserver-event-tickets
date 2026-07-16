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
    public void ReleaseVersionMetadataIsSynchronized()
    {
        const string expected = "1.6.2";
        var project = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "BTCPayServer.Plugins.MakePay.EventTickets.csproj"));
        var plugin = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "EventTicketsPlugin.cs"));
        using var package = JsonDocument.Parse(File.ReadAllText(RepositoryFile(
            "packaging", "BTCPayServer.Plugins.MakePay.EventTickets.json")));

        Assert.Contains($"<Version>{expected}</Version>", project, StringComparison.Ordinal);
        Assert.Contains($"PluginVersion = \"{expected}\"", plugin, StringComparison.Ordinal);
        Assert.Equal(expected + ".0", package.RootElement.GetProperty("Version").GetString());
    }

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
    public void ScannerAccessTokenIsRandomProtectedAndComparedInConstantTime()
    {
        var codes = new TicketCodeService(new EphemeralDataProtectionProvider());
        var item = Event();

        var token = codes.EnsureScannerAccessToken(item);

        Assert.NotEmpty(token);
        Assert.NotEqual(token, item.ProtectedScannerAccessToken);
        Assert.Equal(TicketCodeService.HashScannerAccessToken(token), item.ScannerAccessTokenHash);
        Assert.True(TicketCodeService.CanAccessScanner(item, token));
        Assert.Equal(token, codes.GetScannerAccessToken(item));
        Assert.Equal(token, codes.EnsureScannerAccessToken(item));
        Assert.False(TicketCodeService.CanAccessScanner(item, null));
        Assert.False(TicketCodeService.CanAccessScanner(item, token + "tampered"));

        var rotated = codes.RotateScannerAccessToken(item);
        Assert.NotEqual(token, rotated);
        Assert.False(TicketCodeService.CanAccessScanner(item, token));
        Assert.True(TicketCodeService.CanAccessScanner(item, rotated));

        var source = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Services", "TicketCodeService.cs"));
        Assert.Contains("CryptographicOperations.FixedTimeEquals(expected, actual)", source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(60, true)]
    [InlineData(-1, false)]
    [InlineData(61, false)]
    public void ScannerResultDelayDefaultsToFiveSecondsAndAcceptsOnlyConfiguredRange(int seconds, bool valid)
    {
        Assert.Equal(5, new EventTicketSettings().ScannerResultSeconds);
        var settings = new EventTicketSettings { ScannerResultSeconds = seconds };
        var errors = new List<ValidationResult>();

        Assert.Equal(valid, Validator.TryValidateObject(settings, new ValidationContext(settings), errors, true));
        if (!valid)
            Assert.Contains(errors, result => result.MemberNames.Contains(nameof(EventTicketSettings.ScannerResultSeconds)));
    }

    [Fact]
    public void ScannerLookupIsReadOnlyAndNormalizesLegacyAdmissionState()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var item = Event();
        var ticket = new IssuedTicket
        {
            Id = "ticket-1",
            EventId = item.Id,
            TicketTypeId = "vip",
            AttendeeName = "Ada Lovelace",
            CodeHash = TicketCodeService.Hash(code),
            CheckedInAt = new DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero),
            CheckedInBy = "legacy-door",
            CheckInGate = "North"
        };
        var tickets = new IssuedTicketCollection { Tickets = { [ticket.Id] = ticket } };

        var result = EventTicketRepository.ApplyLookup(tickets, item, code);

        Assert.True(result.Success);
        Assert.Equal("inside", result.Status);
        Assert.Equal("Ada Lovelace", result.Attendee);
        Assert.Equal("VIP", result.TicketType);
        Assert.True(result.IsInside);
        Assert.Equal(1, result.EntranceCount);
        Assert.Equal(0, ticket.EntranceCount);
        Assert.Equal("legacy-door", ticket.CheckedInBy);
        Assert.Null(ticket.CheckedOutAt);
    }

    [Fact]
    public void ScannerCheckInIsExplicitAtomicAndIdempotent()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var checkedInAt = new DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);
        var item = Event();
        var ticket = Ticket(item, code, "Ada Lovelace", "vip");
        var tickets = Collection(ticket);

        var result = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door@example.com", " Main gate ", null, "operation-1", out var changed,
            checkedInAt);

        Assert.True(result.Success);
        Assert.True(changed);
        Assert.Equal("checked_in", result.Status);
        Assert.True(result.IsInside);
        Assert.Equal(1, result.EntranceCount);
        Assert.Equal(checkedInAt, ticket.CheckedInAt);
        Assert.Equal("door@example.com", ticket.CheckedInBy);
        Assert.Equal("Main gate", ticket.CheckInGate);
        Assert.Equal("operation-1", ticket.LastScannerOperationId);

        var duplicate = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "second-door", null, null, "operation-1", out var duplicateChanged,
            checkedInAt.AddSeconds(1));
        Assert.False(duplicate.Success);
        Assert.False(duplicateChanged);
        Assert.Equal("duplicate", duplicate.Status);
        Assert.Equal(1, duplicate.EntranceCount);
        Assert.Equal(checkedInAt, ticket.CheckedInAt);
        Assert.Equal("door@example.com", ticket.CheckedInBy);
    }

    [Fact]
    public void RequiredIdDecisionIsAuditedWithoutAdmittingARejectedHolder()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var item = Event();
        item.RequireIdCheck = true;
        var ticket = Ticket(item, code, "Ada Lovelace", "vip");
        var tickets = Collection(ticket);
        var at = new DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);

        var required = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door@example.com", "Main", null, "required", out var requiredChanged, at);

        Assert.False(required.Success);
        Assert.False(requiredChanged);
        Assert.Equal("id_check_required", required.Status);
        Assert.True(required.RequireIdCheck);
        Assert.False(required.IsInside);
        Assert.Null(ticket.CheckedInAt);

        var rejected = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door@example.com", "Main", false, "reject-1", out var rejectedChanged,
            at.AddSeconds(1));

        Assert.False(rejected.Success);
        Assert.True(rejectedChanged);
        Assert.Equal("id_rejected", rejected.Status);
        Assert.False(rejected.IsInside);
        Assert.Equal(0, rejected.EntranceCount);
        Assert.Equal(1, rejected.IdRejectedCount);
        Assert.Equal(0, rejected.IdConfirmedCount);
        Assert.False(ticket.LastIdCheckConfirmed);
        Assert.Null(ticket.CheckedInAt);

        var retriedRejection = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door@example.com", "Main", false, "reject-1", out var retryChanged,
            at.AddSeconds(2));
        Assert.False(retryChanged);
        Assert.Equal("id_rejected", retriedRejection.Status);
        Assert.Equal(1, ticket.IdRejectedCount);

        var admitted = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door@example.com", "Main", true, "admit-1", out var admittedChanged,
            at.AddSeconds(3));
        Assert.True(admitted.Success);
        Assert.True(admittedChanged);
        Assert.True(admitted.IsInside);
        Assert.Equal(1, admitted.EntranceCount);
        Assert.Equal(1, admitted.IdConfirmedCount);
        Assert.Equal(1, admitted.IdRejectedCount);
        Assert.True(ticket.LastIdCheckConfirmed);
    }

    [Fact]
    public void CheckOutAllowsReEntryAndCountsEveryEntrance()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var item = Event();
        var ticket = Ticket(item, code, "Grace Hopper", "general");
        var tickets = Collection(ticket);
        var firstEntry = new DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);

        _ = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door-a", "North", null, "entry-1", out var entered, firstEntry);
        var checkout = EventTicketRepository.ApplyCheckOut(
            tickets, item, code, "door-a", " Exit A ", "exit-1", out var checkedOut,
            firstEntry.AddHours(1));

        Assert.True(entered);
        Assert.True(checkedOut);
        Assert.True(checkout.Success);
        Assert.Equal("checked_out", checkout.Status);
        Assert.False(checkout.IsInside);
        Assert.Equal(1, checkout.EntranceCount);
        Assert.Equal("door-a", ticket.CheckedOutBy);
        Assert.Equal("Exit A", ticket.CheckOutGate);

        var duplicateCheckout = EventTicketRepository.ApplyCheckOut(
            tickets, item, code, "door-b", null, "exit-1", out var duplicateChanged,
            firstEntry.AddHours(1).AddSeconds(1));
        Assert.False(duplicateCheckout.Success);
        Assert.False(duplicateChanged);
        Assert.Equal("not_checked_in", duplicateCheckout.Status);
        Assert.Equal(1, ticket.EntranceCount);

        var reentry = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door-b", "South", null, "entry-2", out var reentered,
            firstEntry.AddHours(2));
        Assert.True(reentered);
        Assert.True(reentry.Success);
        Assert.True(reentry.IsInside);
        Assert.Equal(2, reentry.EntranceCount);
        Assert.Equal(2, ticket.EntranceCount);
        Assert.Equal("South", ticket.CheckInGate);
    }

    [Fact]
    public void LegacyCheckedInTicketCanCheckOutAndReEnterWithoutMigration()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var item = Event();
        var legacyEntry = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        var ticket = Ticket(item, code, "Legacy Holder", "general");
        ticket.CheckedInAt = legacyEntry;
        ticket.EntranceCount = 0;
        var tickets = Collection(ticket);

        var checkout = EventTicketRepository.ApplyCheckOut(
            tickets, item, code, "door", null, "legacy-exit", out var checkedOut, legacyEntry.AddHours(1));
        Assert.True(checkedOut);
        Assert.Equal(1, checkout.EntranceCount);
        Assert.Equal(1, ticket.EntranceCount);

        var reentry = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door", null, null, "legacy-reentry", out var reentered,
            legacyEntry.AddHours(2));
        Assert.True(reentered);
        Assert.Equal(2, reentry.EntranceCount);
    }

    [Fact]
    public void ScannerRejectsTicketsForAnotherEventWithoutLeakingHolderDataOrMutatingTicket()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var requestedEvent = Event();
        requestedEvent.Id = "other-event";
        var ticket = Ticket(Event(), code, "Ada Lovelace", "vip");
        var tickets = Collection(ticket);

        var lookup = EventTicketRepository.ApplyLookup(tickets, requestedEvent, code);
        var checkIn = EventTicketRepository.ApplyCheckIn(
            tickets, requestedEvent, code, "door@example.com", null, null, "wrong-in", out var checkInChanged);
        var checkOut = EventTicketRepository.ApplyCheckOut(
            tickets, requestedEvent, code, "door@example.com", null, "wrong-out", out var checkOutChanged);

        foreach (var result in new[] { lookup, checkIn, checkOut })
        {
            Assert.False(result.Success);
            Assert.Equal("wrong_event", result.Status);
            Assert.Null(result.TicketId);
            Assert.Null(result.Attendee);
            Assert.Null(result.TicketType);
        }
        Assert.False(checkInChanged);
        Assert.False(checkOutChanged);
        Assert.Null(ticket.CheckedInAt);
        Assert.Null(ticket.CheckedInBy);
    }

    [Fact]
    public void ScannerRejectsRevokedAndUnknownTicketsWithoutCheckingThemIn()
    {
        const string code = "TKT-AAAA-BBBB-CCCC-DDDD";
        var item = Event();
        var ticket = Ticket(item, code, "Grace Hopper", "general");
        ticket.Revoked = true;
        var tickets = Collection(ticket);

        var revokedLookup = EventTicketRepository.ApplyLookup(tickets, item, code);
        var revoked = EventTicketRepository.ApplyCheckIn(
            tickets, item, code, "door@example.com", null, null, "revoked", out var revokedChanged);
        var unknown = EventTicketRepository.ApplyCheckOut(
            tickets, item, "TKT-NOT-A-TICKET", "door@example.com", null, "unknown", out var unknownChanged);

        Assert.False(revokedLookup.Success);
        Assert.Equal("revoked", revokedLookup.Status);
        Assert.False(revoked.Success);
        Assert.Equal("revoked", revoked.Status);
        Assert.Equal("Grace Hopper", revoked.Attendee);
        Assert.Equal("General", revoked.TicketType);
        Assert.False(revokedChanged);
        Assert.False(unknown.Success);
        Assert.Equal("not_found", unknown.Status);
        Assert.False(unknownChanged);
        Assert.Null(ticket.CheckedInAt);
    }

    [Fact]
    public void ScannerUrlsArePerEventAndEscapeTheCapabilityToken()
    {
        const string token = "staff secret+/=";
        var escapedToken = Uri.EscapeDataString(token);

        Assert.Equal(
            $"/stores/store-1/events/builder%20summit/scanner?scannerToken={escapedToken}",
            TicketPublicUrl.ScannerPath(false, "store-1", "builder summit", token));
        Assert.Equal(
            $"/events/builder%20summit/scanner?scannerToken={escapedToken}",
            TicketPublicUrl.ScannerPath(true, "store-1", "builder summit", token));
        Assert.Equal(
            "/events/builder%20summit/scanner",
            TicketPublicUrl.ScannerPath(true, "store-1", "builder summit"));
    }

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
    public void EndedOrUnpublishedEventsCannotStartCheckout()
    {
        var now = DateTimeOffset.UtcNow;
        var item = Event();

        item.EndsAt = now.AddMinutes(1);
        Assert.True(TicketEventSalePolicy.CanStartCheckout(item, now));

        item.EndsAt = now;
        Assert.False(TicketEventSalePolicy.CanStartCheckout(item, now));

        item.EndsAt = now.AddMinutes(1);
        item.Published = false;
        Assert.False(TicketEventSalePolicy.CanStartCheckout(item, now));
    }

    [Fact]
    public void SensitivePublicActionsExplicitlyDisableBrowserAndProxyCaching()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Controllers", "EventTicketsPublicController.cs"));
        var sensitiveActions = new[]
        {
            "StartCheckout", "Rebuy", "Cart", "ApplyPromotion", "Details", "CreatePayment",
            "Payment", "OrderStatus", "Order", "Pdf", "AppleWallet"
        };

        foreach (var actionName in sensitiveActions)
        {
            var signature = $"public async Task<IActionResult> {actionName}";
            var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(signatureIndex >= 0, $"Could not find public action {actionName}.");
            var attributeBlock = source[Math.Max(0, signatureIndex - 160)..signatureIndex];
            Assert.Contains("[TicketNoStore]", attributeBlock, StringComparison.Ordinal);
        }

        Assert.Contains("headers[\"Cache-Control\"] = \"no-store, no-cache, private, max-age=0, must-revalidate\"", source, StringComparison.Ordinal);
        Assert.Contains("headers[\"Pragma\"] = \"no-cache\"", source, StringComparison.Ordinal);
        Assert.Contains("headers[\"Expires\"] = \"0\"", source, StringComparison.Ordinal);
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

    [Fact]
    public void AdminOrderQueryFiltersTheFullCollectionBeforePaginating()
    {
        var eventA = Event();
        var eventB = Event();
        eventB.Id = "event-b";
        eventB.Name = "Second Event";
        var createdAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var orders = Enumerable.Range(0, 30)
            .Select(index => new TicketOrder
            {
                Id = $"paid-{index:D2}",
                EventId = eventA.Id,
                Status = TicketOrderStatus.Paid,
                CreatedAt = createdAt.AddMinutes(index)
            })
            .Concat(Enumerable.Range(0, 10).Select(index => new TicketOrder
            {
                Id = $"other-event-{index:D2}",
                EventId = eventB.Id,
                Status = TicketOrderStatus.Paid,
                CreatedAt = createdAt.AddHours(2).AddMinutes(index)
            }))
            .Concat(Enumerable.Range(0, 5).Select(index => new TicketOrder
            {
                Id = $"cancelled-{index:D2}",
                EventId = eventA.Id,
                Status = TicketOrderStatus.Cancelled,
                CreatedAt = createdAt.AddHours(3).AddMinutes(index)
            }))
            .ToList();

        var page = EventTicketOrderQueryService.Apply(orders, [eventA, eventB], new EventTicketOrderQuery
        {
            OrderEventId = eventA.Id,
            OrderStatus = "paid",
            OrderPage = 2,
            OrderPageSize = 10
        });

        Assert.Equal(30, page.TotalItems);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Page);
        Assert.Equal(11, page.FirstItem);
        Assert.Equal(20, page.LastItem);
        Assert.Equal(10, page.Items.Count);
        Assert.All(page.Items, order =>
        {
            Assert.Equal(eventA.Id, order.EventId);
            Assert.Equal(TicketOrderStatus.Paid, order.Status);
        });
        Assert.Equal("paid-19", page.Items[0].Id);
    }

    [Theory]
    [InlineData("order-special")]
    [InlineData("invoice-special")]
    [InlineData("Ada Buyer")]
    [InlineData("buyer@example.com")]
    [InlineData("Grace Hopper")]
    [InlineData("attendee@example.com")]
    [InlineData("Builder Summit")]
    [InlineData("event")]
    public void AdminOrderQuerySearchesOrderInvoiceAndCustomerFields(string search)
    {
        var item = Event();
        var order = new TicketOrder
        {
            Id = "order-special",
            InvoiceId = "invoice-special",
            EventId = item.Id,
            BuyerName = "Ada Buyer",
            BuyerEmail = "buyer@example.com",
            Attendees =
            [
                new TicketAttendee
                {
                    FirstName = "Grace",
                    LastName = "Hopper",
                    Email = "attendee@example.com"
                }
            ]
        };

        var page = EventTicketOrderQueryService.Apply([order], [item], new EventTicketOrderQuery
        {
            OrderSearch = $"  {search.ToUpperInvariant()}  "
        });

        Assert.Single(page.Items);
        Assert.Equal(search.ToUpperInvariant(), page.Search);
    }

    [Fact]
    public void AdminOrderQueryClampsUnsupportedAndOutOfRangeParameters()
    {
        var item = Event();
        var orders = Enumerable.Range(0, 45).Select(index => new TicketOrder
        {
            Id = $"order-{index}",
            EventId = item.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(index)
        }).ToList();

        var lastPage = EventTicketOrderQueryService.Apply(orders, [item], new EventTicketOrderQuery
        {
            OrderSearch = "   ",
            OrderEventId = "unknown-event",
            OrderStatus = "999",
            OrderPage = int.MaxValue,
            OrderPageSize = 17
        });
        var firstPage = EventTicketOrderQueryService.Apply(orders, [item], new EventTicketOrderQuery
        {
            OrderPage = -100,
            OrderPageSize = 10
        });

        Assert.Equal(25, lastPage.PageSize);
        Assert.Equal(2, lastPage.Page);
        Assert.Equal(20, lastPage.Items.Count);
        Assert.False(lastPage.IsFiltered);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(10, firstPage.Items.Count);
    }

    [Fact]
    public void AdminOrderQueryUsesOrderIdAsAStableSecondarySort()
    {
        var item = Event();
        var createdAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var orders = new[] { "order-a", "order-c", "order-b" }
            .Select(id => new TicketOrder { Id = id, EventId = item.Id, CreatedAt = createdAt })
            .ToList();

        var page = EventTicketOrderQueryService.Apply(orders, [item], new EventTicketOrderQuery());

        Assert.Equal(["order-c", "order-b", "order-a"], page.Items.Select(order => order.Id));
    }

    [Fact]
    public void AdminOrderQueryReturnsSafeMetadataForAnEmptyCollection()
    {
        var page = EventTicketOrderQueryService.Apply([], [Event()], new EventTicketOrderQuery
        {
            OrderPage = 99,
            OrderPageSize = 100
        });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(1, page.Page);
        Assert.Equal(0, page.FirstItem);
        Assert.Equal(0, page.LastItem);
        Assert.False(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void AdminOrderDetailProjectsLineAttendeeAndAdmissionDataWithoutTicketSecrets()
    {
        var item = Event();
        var order = new TicketOrder
        {
            Id = "order-1",
            EventId = item.Id,
            Lines = [new TicketOrderLine { TicketTypeId = "vip", Quantity = 2, UnitPrice = 60m }],
            Attendees =
            [
                new TicketAttendee
                {
                    TicketTypeId = "vip",
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Nickname = "Ada",
                    Email = "ada@example.com",
                    Phone = "+971500000000",
                    Country = "AE",
                    Company = "Analytical Engines"
                }
            ]
        };
        var issuedAt = new DateTimeOffset(2026, 7, 16, 8, 0, 0, TimeSpan.Zero);
        var ticket = new IssuedTicket
        {
            Id = "ticket-1",
            OrderId = order.Id,
            EventId = item.Id,
            TicketTypeId = "vip",
            AttendeeName = "Ada Lovelace",
            AttendeeEmail = "ada@example.com",
            IssuedAt = issuedAt,
            CheckedInAt = issuedAt.AddHours(1),
            CheckedInBy = "door@example.com",
            CheckInGate = "Main gate",
            EntranceCount = 2,
            IdConfirmedCount = 2,
            IdRejectedCount = 1,
            LastIdCheckedAt = issuedAt.AddMinutes(50),
            LastIdCheckedBy = "door@example.com",
            LastIdCheckConfirmed = true,
            ProtectedCode = "must-not-project",
            CodeHash = "must-not-project",
            LastScannerOperationId = "must-not-project"
        };
        var unrelated = new IssuedTicket { Id = "ticket-2", OrderId = "another-order", IssuedAt = issuedAt };
        var query = new EventTicketOrderQuery { OrderSearch = "Ada", OrderPage = 2, OrderPageSize = 10 };

        var detail = EventTicketOrderDetailService.Build("store", order, item, [unrelated, ticket], query);

        var line = Assert.Single(detail.Lines);
        Assert.Equal("VIP", line.TicketTypeName);
        Assert.Equal(120m, line.Total);
        var attendee = Assert.Single(detail.Attendees);
        Assert.Equal("Ada Lovelace", attendee.Name);
        Assert.Equal("VIP", attendee.TicketTypeName);
        var admission = Assert.Single(detail.Tickets);
        Assert.Equal("ticket-1", admission.TicketId);
        Assert.True(admission.IsInside);
        Assert.Equal(2, admission.EntranceCount);
        Assert.Equal("Main gate", admission.LastGate);
        Assert.Equal(ticket.CheckedInAt, admission.LastActivityAt);
        Assert.Same(query, detail.ReturnQuery);
        Assert.Equal(order.Id, detail.Order.Id);
        Assert.Equal(order.Total, detail.Order.Total);
        Assert.Equal(item.Id, detail.Event?.Id);

        var projectedProperties = typeof(EventTicketIssuedTicketDetail).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(IssuedTicket.ProtectedCode), projectedProperties);
        Assert.DoesNotContain(nameof(IssuedTicket.CodeHash), projectedProperties);
        Assert.DoesNotContain(nameof(IssuedTicket.LastScannerOperationId), projectedProperties);

        var orderProperties = typeof(EventTicketAdminOrderDetail).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(TicketOrder.ProtectedPublicAccessToken), orderProperties);
        Assert.DoesNotContain(nameof(TicketOrder.PublicAccessTokenHash), orderProperties);
        Assert.DoesNotContain(nameof(TicketOrder.PublicBaseUrl), orderProperties);

        var eventProperties = typeof(EventTicketAdminEventDetail).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(TicketEvent.ProtectedScannerAccessToken), eventProperties);
        Assert.DoesNotContain(nameof(TicketEvent.ScannerAccessTokenHash), eventProperties);
    }

    [Fact]
    public void AdminOrderDetailRetainsLegacyLinesWhenTheEventWasDeleted()
    {
        var order = new TicketOrder
        {
            Id = "legacy-order",
            TicketTypeId = "deleted-type",
            Quantity = 3,
            Subtotal = 45m
        };

        var detail = EventTicketOrderDetailService.Build("store", order, null, []);

        Assert.Null(detail.Event);
        var line = Assert.Single(detail.Lines);
        Assert.Equal("Deleted ticket type", line.TicketTypeName);
        Assert.Equal("deleted-type", line.TicketTypeId);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(15m, line.UnitPrice);
        Assert.Equal(45m, line.Total);
        Assert.Empty(detail.Attendees);
        Assert.Empty(detail.Tickets);
    }

    [Fact]
    public void AdminOrderDetailUsesLegacyOrderAmountsInsteadOfAChangedCurrentTicketPrice()
    {
        var item = Event();
        item.TicketTypes.Single(type => type.Id == "general").Price = 99m;
        var order = new TicketOrder
        {
            Id = "legacy-order",
            EventId = item.Id,
            TicketTypeId = "general",
            Quantity = 3,
            Subtotal = 45m,
            Total = 45m
        };

        var line = Assert.Single(EventTicketOrderDetailService.Build("store", order, item, []).Lines);

        Assert.Equal("General", line.TicketTypeName);
        Assert.Equal(15m, line.UnitPrice);
        Assert.Equal(45m, line.Total);
    }

    [Fact]
    public void AdminOrderDetailDoesNotFabricateAmountsOrExpiryForAVersionOneOrder()
    {
        var item = Event();
        item.TicketTypes.Single(type => type.Id == "general").Price = 99m;
        var order = new TicketOrder
        {
            Id = "v1-order",
            EventId = item.Id,
            TicketTypeId = "general",
            Quantity = 2,
            InvoiceId = "legacy-invoice",
            Status = TicketOrderStatus.Cancelled
        };

        var detail = EventTicketOrderDetailService.Build("store", order, item, []);

        var line = Assert.Single(detail.Lines);
        Assert.Null(line.UnitPrice);
        Assert.Null(line.Total);
        Assert.Null(detail.Order.Currency);
        Assert.Null(detail.Order.Subtotal);
        Assert.Null(detail.Order.DiscountAmount);
        Assert.Null(detail.Order.Total);
        Assert.Null(detail.Order.ReservationExpiresAt);
        Assert.False(detail.Order.AmountsFromInvoice);
    }

    [Fact]
    public void AdminOrderDetailRecoversAVersionOneTotalFromItsTenantCheckedInvoiceSnapshot()
    {
        var item = Event();
        item.TicketTypes.Single(type => type.Id == "general").Price = 99m;
        var order = new TicketOrder
        {
            Id = "v1-order",
            EventId = item.Id,
            TicketTypeId = "general",
            Quantity = 2,
            InvoiceId = "legacy-invoice",
            Status = TicketOrderStatus.Paid
        };

        var detail = EventTicketOrderDetailService.Build("store", order, item, [], invoice: new(42m, "AED"));

        var line = Assert.Single(detail.Lines);
        Assert.Equal(21m, line.UnitPrice);
        Assert.Equal(42m, line.Total);
        Assert.Equal("AED", detail.Order.Currency);
        Assert.Null(detail.Order.Subtotal);
        Assert.Null(detail.Order.DiscountAmount);
        Assert.Equal(42m, detail.Order.Total);
        Assert.Null(detail.Order.ReservationExpiresAt);
        Assert.True(detail.Order.AmountsFromInvoice);
    }

    private static TicketCheckoutService Checkout(out TicketCodeService codes)
    {
        codes = new TicketCodeService(new EphemeralDataProtectionProvider());
        return new TicketCheckoutService(codes);
    }

    private static IssuedTicket Ticket(TicketEvent item, string code, string attendee, string ticketTypeId) => new()
    {
        Id = "ticket-1",
        EventId = item.Id,
        TicketTypeId = ticketTypeId,
        AttendeeName = attendee,
        CodeHash = TicketCodeService.Hash(code)
    };

    private static IssuedTicketCollection Collection(IssuedTicket ticket) => new()
    {
        Tickets = { [ticket.Id] = ticket }
    };

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
