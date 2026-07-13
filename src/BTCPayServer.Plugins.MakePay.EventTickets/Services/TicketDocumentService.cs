#nullable enable
using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using QRCoder;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class TicketDocumentService(TicketCodeService codes)
{
    public string QrDataUri(string storeId, string code)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(TicketCodeService.QrPayload(storeId, code), QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    public byte[] CreatePdf(TicketEvent item, TicketOrder order, IReadOnlyList<IssuedTicket> tickets, EventTicketSettings? settings = null)
    {
        var pages = tickets.Select(ticket =>
        {
            var code = codes.Unprotect(ticket.ProtectedCode) ?? "UNAVAILABLE";
            var type = item.TicketTypes.FirstOrDefault(ticketType => ticketType.Id == ticket.TicketTypeId);
            return new PdfTicket(ticket, type?.Name ?? "Event ticket", code, CreateQrBits(TicketCodeService.QrPayload(order.StoreId, code)));
        }).ToList();
        if (pages.Count == 0) pages.Add(new PdfTicket(new IssuedTicket { AttendeeName = order.BuyerName }, "Order pending", order.Id, CreateQrBits(order.Id)));
        return BuildPdf(item, order, pages, settings ?? new EventTicketSettings());
    }

    private static byte[] BuildPdf(TicketEvent item, TicketOrder order, IReadOnlyList<PdfTicket> tickets, EventTicketSettings settings)
    {
        var objects = new List<byte[]>();
        var pageIds = Enumerable.Range(0, tickets.Count).Select(index => 7 + index * 3).ToList();
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"));

        var accent = PdfColor.FromHex(settings.AccentColor, new PdfColor(.08, .37, .94));
        var accentText = PdfColor.FromHex(settings.AccentTextColor, PdfColor.White);
        var text = PdfColor.FromHex(settings.TextColor, new PdfColor(.06, .09, .16));
        var muted = PdfColor.FromHex(settings.MutedColor, new PdfColor(.4, .44, .52));
        var soft = accent.Mix(PdfColor.White, .92);

        for (var index = 0; index < tickets.Count; index++)
        {
            var pdfTicket = tickets[index];
            var imageId = 5 + index * 3;
            var contentId = 6 + index * 3;
            var qrName = "QR" + (index + 1);
            objects.Add(StreamObject($"<< /Type /XObject /Subtype /Image /Width {pdfTicket.Qr.Width} /Height {pdfTicket.Qr.Height} /ColorSpace /DeviceGray /BitsPerComponent 1 /Decode [1 0] /Filter /FlateDecode /Length {{0}} >>", Compress(pdfTicket.Qr.Data)));

            var content = new StringBuilder();
            FillRect(content, accent, 0, 590, 612, 202);
            FillRect(content, soft, 36, 230, 540, 322);
            StrokeRect(content, accent.Mix(PdfColor.White, .62), 36, 230, 540, 322, 1);
            FillRect(content, PdfColor.White, 354, 328, 198, 198);
            AddText(content, "F2", 11, 44, 756, accentText, settings.StorefrontTitle.ToUpperInvariant());
            AddWrappedText(content, "F2", 28, 44, 710, accentText, item.Name, 31, 32, 2);
            AddText(content, "F1", 12, 44, 612, accentText, item.StartsAt.ToString("dddd, dd MMMM yyyy - HH:mm", CultureInfo.InvariantCulture));
            AddText(content, "F2", 12, 50, 524, accent, pdfTicket.TypeName.ToUpperInvariant());
            AddText(content, "F2", 9, 50, 479, muted, "ATTENDEE");
            AddWrappedText(content, "F2", 22, 50, 446, text, pdfTicket.Ticket.AttendeeName, 26, 25, 2);
            AddText(content, "F2", 9, 50, 385, muted, "VENUE");
            AddWrappedText(content, "F1", 12, 50, 362, text, item.VenueName + " - " + item.VenueAddress, 48, 16, 3);
            content.Append($"q\n178 0 0 178 364 338 cm\n/{qrName} Do\nQ\n");
            AddText(content, "F2", 9, 390, 310, muted, "SCAN AT ENTRANCE");
            AddText(content, "F2", 9, 50, 202, muted, "TICKET CODE");
            FillRect(content, soft, 42, 145, 528, 42);
            AddWrappedText(content, "F2", 11, 54, 163, text, pdfTicket.Code, 66, 14, 2);
            AddText(content, "F1", 9, 44, 104, muted, $"Order {order.Id}");
            AddText(content, "F1", 9, 44, 84, muted, "Keep this ticket private. The QR code is valid for one admission.");
            AddText(content, "F2", 9, 44, 48, accent, "CREATED WITH MAKEPAY.IO + BTCPAY SERVER");
            objects.Add(StreamObject("<< /Length {0} >>", Encoding.ASCII.GetBytes(content.ToString())));
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /{qrName} {imageId} 0 R >> >> /Contents {contentId} 0 R >>"));
        }

        using var output = new MemoryStream();
        Write(output, Ascii("%PDF-1.4\n%MPET\n"));
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            Write(output, Ascii($"{index + 1} 0 obj\n"));
            Write(output, objects[index]);
            Write(output, Ascii("\nendobj\n"));
        }
        var xref = output.Position;
        Write(output, Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1)) Write(output, Ascii($"{offset:0000000000} 00000 n \n"));
        Write(output, Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    private static void AddText(StringBuilder content, string font, int size, double x, double y, PdfColor color, string raw)
    {
        content.Append($"BT\n/{font} {size} Tf\n{color.Fill()} rg\n{N(x)} {N(y)} Td\n({Escape(AsciiText(raw))}) Tj\nET\n");
    }

    private static void AddWrappedText(StringBuilder content, string font, int size, double x, double y, PdfColor color, string raw, int maxCharacters, int lineHeight, int maxLines)
    {
        var lines = Wrap(AsciiText(raw), maxCharacters).Take(maxLines).ToList();
        for (var index = 0; index < lines.Count; index++) AddText(content, font, size, x, y - index * lineHeight, color, lines[index]);
    }

    private static void FillRect(StringBuilder content, PdfColor color, double x, double y, double width, double height)
    {
        content.Append($"q\n{color.Fill()} rg\n{N(x)} {N(y)} {N(width)} {N(height)} re f\nQ\n");
    }

    private static void StrokeRect(StringBuilder content, PdfColor color, double x, double y, double width, double height, double lineWidth)
    {
        content.Append($"q\n{color.Fill()} RG\n{N(lineWidth)} w\n{N(x)} {N(y)} {N(width)} {N(height)} re S\nQ\n");
    }

    private static IEnumerable<string> Wrap(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > max)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    private static QrBits CreateQrBits(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var quiet = 4;
        var width = data.ModuleMatrix.Count + quiet * 2;
        var stride = (width + 7) / 8;
        var bytes = new byte[stride * width];
        for (var y = 0; y < data.ModuleMatrix.Count; y++)
        {
            BitArray row = data.ModuleMatrix[y];
            for (var x = 0; x < row.Count; x++)
            {
                if (!row[x]) continue;
                var targetX = x + quiet;
                var targetY = y + quiet;
                bytes[targetY * stride + targetX / 8] |= (byte)(1 << (7 - targetX % 8));
            }
        }
        return new QrBits(width, width, bytes);
    }

    private static byte[] Compress(byte[] value)
    {
        using var target = new MemoryStream();
        using (var zlib = new ZLibStream(target, CompressionLevel.Optimal, true)) zlib.Write(value);
        return target.ToArray();
    }

    private static byte[] StreamObject(string dictionary, byte[] content)
    {
        var prefix = Ascii(dictionary.Replace("{0}", content.Length.ToString(), StringComparison.Ordinal) + "\nstream\n");
        var suffix = Ascii("\nendstream");
        var output = new byte[prefix.Length + content.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, output, 0, prefix.Length);
        Buffer.BlockCopy(content, 0, output, prefix.Length, content.Length);
        Buffer.BlockCopy(suffix, 0, output, prefix.Length + content.Length, suffix.Length);
        return output;
    }

    private static void Write(Stream target, byte[] value) => target.Write(value, 0, value.Length);
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static string AsciiText(string value) => new(value.Select(character => character is >= ' ' and <= '~' ? character : '-').ToArray());
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private sealed record PdfColor(double R, double G, double B)
    {
        public static PdfColor White { get; } = new(1, 1, 1);
        public string Fill() => $"{N(R)} {N(G)} {N(B)}";
        public PdfColor Mix(PdfColor other, double amount) => new(R + (other.R - R) * amount, G + (other.G - G) * amount, B + (other.B - B) * amount);
        public static PdfColor FromHex(string? value, PdfColor fallback)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#') return fallback;
            return int.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) && int.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) && int.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
                ? new PdfColor(red / 255d, green / 255d, blue / 255d)
                : fallback;
        }
    }
    private sealed record QrBits(int Width, int Height, byte[] Data);
    private sealed record PdfTicket(IssuedTicket Ticket, string TypeName, string Code, QrBits Qr);
}
