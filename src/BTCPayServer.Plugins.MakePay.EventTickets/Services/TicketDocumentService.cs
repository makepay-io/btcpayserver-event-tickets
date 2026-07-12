#nullable enable
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

    public byte[] CreatePdf(TicketEvent item, TicketType type, TicketOrder order, IReadOnlyList<IssuedTicket> tickets)
    {
        var lines = new List<string>
        {
            item.Name,
            $"{item.StartsAt:u} - {item.EndsAt:u}",
            item.VenueName,
            item.VenueAddress,
            $"Order {order.Id}",
            ""
        };
        foreach (var ticket in tickets)
        {
            var code = codes.Unprotect(ticket.ProtectedCode) ?? "UNAVAILABLE";
            lines.Add($"{type.Name} - {ticket.AttendeeName}");
            lines.Add(code);
            lines.Add("");
        }
        return MinimalPdf(lines);
    }

    private static byte[] MinimalPdf(IEnumerable<string> lines)
    {
        var content = new StringBuilder("BT\n/F1 18 Tf\n50 760 Td\n");
        var first = true;
        foreach (var raw in lines.Take(30))
        {
            var line = Escape(Ascii(raw));
            if (!first) content.Append("0 -23 Td\n");
            content.Append('(').Append(line).Append(") Tj\n");
            first = false;
        }
        content.Append("ET\n");
        var stream = content.ToString();
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        using var output = new MemoryStream();
        void Write(string value) { var bytes = Encoding.ASCII.GetBytes(value); output.Write(bytes); }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(output.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = output.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
    private static string Ascii(string value) => new(value.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
}
