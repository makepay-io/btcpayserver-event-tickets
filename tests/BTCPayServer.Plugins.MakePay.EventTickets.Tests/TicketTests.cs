using System.Text;
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

    private static TicketCheckoutService Checkout(out TicketCodeService codes)
    {
        codes = new TicketCodeService(new EphemeralDataProtectionProvider());
        return new TicketCheckoutService(codes);
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
