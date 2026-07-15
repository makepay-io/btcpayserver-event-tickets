#nullable enable
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class TicketCodeService(IDataProtectionProvider provider)
{
    public const string ScannerSessionCookieName = "makepay_event_scanner";
    private readonly IDataProtector _protector = provider.CreateProtector("MakePay.EventTickets.v1");
    public (string Code, string Hash, string Protected) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var code = "TKT-" + Convert.ToHexString(bytes)[..10] + "-" + Convert.ToHexString(bytes)[10..20] + "-" + Convert.ToHexString(bytes)[20..30] + "-" + Convert.ToHexString(bytes)[30..40];
        return (code, Hash(code), _protector.Protect(code));
    }
    public string Protect(string value) => _protector.Protect(value);
    public string? Unprotect(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; try { return _protector.Unprotect(value); } catch (CryptographicException) { return null; } }
    public string EnsureScannerAccessToken(TicketEvent item)
    {
        var existing = GetScannerAccessToken(item);
        if (existing is not null) return existing;
        return RotateScannerAccessToken(item);
    }
    public string RotateScannerAccessToken(TicketEvent item)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        item.ProtectedScannerAccessToken = Protect(token);
        item.ScannerAccessTokenHash = HashScannerAccessToken(token);
        return token;
    }
    public string? GetScannerAccessToken(TicketEvent item)
    {
        var token = Unprotect(item.ProtectedScannerAccessToken);
        return CanAccessScanner(item, token) ? token : null;
    }
    public static bool CanAccessScanner(TicketEvent item, string? token)
    {
        if (string.IsNullOrWhiteSpace(item.ScannerAccessTokenHash) || string.IsNullOrWhiteSpace(token)) return false;
        var expected = Encoding.ASCII.GetBytes(item.ScannerAccessTokenHash);
        var actual = Encoding.ASCII.GetBytes(HashScannerAccessToken(token));
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
    public static string HashScannerAccessToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string Normalize(string value) => value.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value))));
    public static string QrPayload(string storeId, string code) => $"MPET:{storeId}:{Normalize(code)}";
    public static string ExtractCode(string payload) { var parts = payload.Trim().Split(':', 3); return parts.Length == 3 && parts[0] == "MPET" ? parts[2] : payload.Trim(); }
}
