#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class WalletPassService(TicketCodeService secrets)
{
    private const string PixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl8gS8AAAAASUVORK5CYII=";
    public bool AppleConfigured(EventTicketSettings settings) => !string.IsNullOrWhiteSpace(settings.ApplePassTypeIdentifier) && !string.IsNullOrWhiteSpace(settings.AppleTeamIdentifier) && settings.ProtectedAppleP12 is not null;
    public bool GoogleConfigured(EventTicketSettings settings) => !string.IsNullOrWhiteSpace(settings.GoogleIssuerId) && !string.IsNullOrWhiteSpace(settings.GoogleClassId) && settings.ProtectedGoogleServiceAccountJson is not null;

    public byte[] CreateApplePass(EventTicketSettings settings, TicketEvent item, TicketType type, IssuedTicket ticket, string code)
    {
        var p12 = Convert.FromBase64String(secrets.Unprotect(settings.ProtectedAppleP12) ?? throw new InvalidOperationException("Apple certificate is unavailable."));
        var password = secrets.Unprotect(settings.ProtectedAppleP12Password) ?? "";
        var certificate = new X509Certificate2(p12, password, X509KeyStorageFlags.Exportable);
        var pass = new
        {
            formatVersion = 1,
            passTypeIdentifier = settings.ApplePassTypeIdentifier,
            serialNumber = ticket.Id,
            teamIdentifier = settings.AppleTeamIdentifier,
            organizationName = settings.AppleOrganizationName,
            description = item.Name,
            logoText = item.Name,
            foregroundColor = "rgb(255,255,255)",
            backgroundColor = HexToRgb(settings.AccentColor),
            groupingIdentifier = item.Id,
            relevantDate = item.StartsAt.UtcDateTime.ToString("O"),
            barcode = new { format = "PKBarcodeFormatQR", message = TicketCodeService.QrPayload(ticket.StoreId, code), messageEncoding = "iso-8859-1", altText = code },
            eventTicket = new
            {
                primaryFields = new[] { new { key = "event", label = "EVENT", value = item.Name } },
                secondaryFields = new[] { new { key = "date", label = "DATE", value = item.StartsAt.ToString("ddd, dd MMM yyyy HH:mm") }, new { key = "type", label = "TICKET", value = type.Name } },
                auxiliaryFields = new[] { new { key = "venue", label = "VENUE", value = item.VenueName }, new { key = "attendee", label = "ATTENDEE", value = ticket.AttendeeName } },
                backFields = new[] { new { key = "address", label = "ADDRESS", value = item.VenueAddress }, new { key = "ticket", label = "TICKET ID", value = ticket.Id } }
            }
        };
        var files = new Dictionary<string, byte[]>
        {
            ["pass.json"] = JsonSerializer.SerializeToUtf8Bytes(pass, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ["icon.png"] = Convert.FromBase64String(PixelPng),
            ["icon@2x.png"] = Convert.FromBase64String(PixelPng),
            ["logo.png"] = Convert.FromBase64String(PixelPng)
        };
        var manifest = files.ToDictionary(pair => pair.Key, pair => Convert.ToHexString(SHA1.HashData(pair.Value)).ToLowerInvariant());
        files["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var cms = new SignedCms(new ContentInfo(files["manifest.json"]), true);
        cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate) { IncludeOption = X509IncludeOption.EndCertOnly });
        files["signature"] = cms.Encode();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var file in files) { var entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal); using var target = entry.Open(); target.Write(file.Value); }
        return output.ToArray();
    }

    public string CreateGoogleSaveUrl(EventTicketSettings settings, TicketEvent item, TicketType type, IssuedTicket ticket, string code)
    {
        var json = secrets.Unprotect(settings.ProtectedGoogleServiceAccountJson) ?? throw new InvalidOperationException("Google service account is unavailable.");
        using var doc = JsonDocument.Parse(json);
        var email = doc.RootElement.GetProperty("client_email").GetString()!;
        var privateKey = doc.RootElement.GetProperty("private_key").GetString()!;
        var objectId = settings.GoogleIssuerId + "." + new string(ticket.Id.Where(char.IsLetterOrDigit).ToArray());
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = email, aud = "google", typ = "savetowallet", iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            payload = new { genericObjects = new[] { new { id = objectId, classId = settings.GoogleClassId, hexBackgroundColor = settings.AccentColor, cardTitle = new { @defaultValue = new { language = "en-US", value = item.Name } }, header = new { @defaultValue = new { language = "en-US", value = type.Name } }, subheader = new { @defaultValue = new { language = "en-US", value = item.StartsAt.ToString("ddd, dd MMM yyyy HH:mm") } }, barcode = new { type = "QR_CODE", value = TicketCodeService.QrPayload(ticket.StoreId, code), alternateText = code }, textModulesData = new[] { new { id = "venue", header = "Venue", body = item.VenueName + "\n" + item.VenueAddress }, new { id = "attendee", header = "Attendee", body = ticket.AttendeeName } } } } }
        }));
        var input = header + "." + payload;
        using var rsa = RSA.Create(); rsa.ImportFromPem(privateKey);
        var signature = Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return "https://pay.google.com/gp/v/save/" + input + "." + signature;
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string HexToRgb(string hex) { var raw = hex.TrimStart('#'); return raw.Length == 6 ? $"rgb({Convert.ToInt32(raw[..2],16)},{Convert.ToInt32(raw[2..4],16)},{Convert.ToInt32(raw[4..],16)})" : "rgb(234,88,12)"; }
}
