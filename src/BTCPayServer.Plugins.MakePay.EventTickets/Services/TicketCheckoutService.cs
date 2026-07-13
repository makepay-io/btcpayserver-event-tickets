#nullable enable
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class TicketCheckoutService(TicketCodeService secrets)
{
    public string CreateAccessToken(TicketOrder order)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        order.ProtectedPublicAccessToken = secrets.Protect(token);
        order.PublicAccessTokenHash = HashAccessToken(token);
        return token;
    }

    public string? GetAccessToken(TicketOrder order) => secrets.Unprotect(order.ProtectedPublicAccessToken);

    public bool CanAccess(TicketOrder order, string? token)
    {
        // Orders created by v1.0 used the random 128-bit order id as their public capability URL.
        if (string.IsNullOrWhiteSpace(order.PublicAccessTokenHash)) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var expected = Encoding.ASCII.GetBytes(order.PublicAccessTokenHash);
        var actual = Encoding.ASCII.GetBytes(HashAccessToken(token));
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static string HashAccessToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static List<TicketOrderLine> BuildLines(TicketEvent item, IReadOnlyDictionary<string, int> quantities)
    {
        var lines = new List<TicketOrderLine>();
        foreach (var type in item.TicketTypes.Where(t => t.Active))
        {
            var quantity = quantities.GetValueOrDefault(type.Id);
            if (quantity == 0) continue;
            if (quantity < 0 || quantity > type.MaxPerOrder) return [];
            lines.Add(new TicketOrderLine { TicketTypeId = type.Id, Quantity = quantity, UnitPrice = type.Price });
        }
        return lines;
    }

    public static List<TicketOrderLine> ResolveLines(TicketOrder order, TicketEvent item)
    {
        if (order.Lines.Count > 0) return order.Lines;
        if (string.IsNullOrWhiteSpace(order.TicketTypeId) || order.Quantity < 1) return [];
        var type = item.TicketTypes.FirstOrDefault(t => t.Id == order.TicketTypeId);
        return type is null ? [] : [new TicketOrderLine { TicketTypeId = type.Id, Quantity = order.Quantity, UnitPrice = type.Price }];
    }

    public static List<TicketLineViewModel> ResolveLineViewModels(TicketOrder order, TicketEvent item) => ResolveLines(order, item)
        .Select(line => new { Line = line, Type = item.TicketTypes.FirstOrDefault(t => t.Id == line.TicketTypeId) })
        .Where(value => value.Type is not null)
        .Select(value => new TicketLineViewModel { Line = value.Line, TicketType = value.Type! })
        .ToList();

    public static void Recalculate(TicketOrder order)
    {
        order.Quantity = order.Lines.Count > 0 ? order.Lines.Sum(line => line.Quantity) : order.Quantity;
        order.TicketTypeId = order.Lines.Count > 0 ? order.Lines[0].TicketTypeId : order.TicketTypeId;
        order.Subtotal = order.Lines.Count > 0 ? order.Lines.Sum(line => line.UnitPrice * line.Quantity) : order.Subtotal;
        order.DiscountAmount = Math.Clamp(order.DiscountAmount, 0, order.Subtotal);
        order.Total = order.Subtotal - order.DiscountAmount;
    }

    public static bool ApplyPromo(TicketOrder order, EventTicketSettings settings, string? submittedCode)
    {
        if (string.IsNullOrWhiteSpace(settings.PromoCode) || settings.PromoPercent <= 0 ||
            !string.Equals(settings.PromoCode.Trim(), submittedCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            order.PromoCode = null;
            order.DiscountAmount = 0;
            Recalculate(order);
            return false;
        }

        order.PromoCode = settings.PromoCode.Trim();
        order.DiscountAmount = decimal.Round(order.Subtotal * settings.PromoPercent / 100m, 2, MidpointRounding.AwayFromZero);
        Recalculate(order);
        return true;
    }
}
