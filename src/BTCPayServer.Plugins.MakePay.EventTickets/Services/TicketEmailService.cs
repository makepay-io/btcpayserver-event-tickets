#nullable enable
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public sealed class TicketEmailService(HttpClient http, EmailSenderFactory emailFactory, TicketCodeService secrets, TicketDocumentService documents, ILogger<TicketEmailService> logger)
{
    public async Task Send(string storeId, EventTicketSettings settings, TicketEvent item, TicketOrder order, IReadOnlyList<IssuedTicket> tickets, string orderUrl, CancellationToken cancellationToken)
    {
        string E(string value) => HtmlEncoder.Default.Encode(value);
        var html = settings.EmailHtml.Replace("{EventName}", E(item.Name), StringComparison.Ordinal).Replace("{EventDate}", E(item.StartsAt.ToString("u")), StringComparison.Ordinal).Replace("{Venue}", E(item.VenueName), StringComparison.Ordinal).Replace("{OrderUrl}", E(orderUrl), StringComparison.Ordinal);
        var subject = settings.EmailSubject.Replace("{EventName}", item.Name, StringComparison.Ordinal);
        var pdf = settings.AttachPdf ? documents.CreatePdf(item, order, tickets, settings) : null;
        if (settings.EmailProvider == TicketEmailProvider.Resend) await SendResend(settings, order.BuyerEmail, subject, html, pdf, cancellationToken); else await SendSmtp(storeId, order.BuyerEmail, subject, html, pdf, cancellationToken);
    }

    private async Task SendResend(EventTicketSettings settings, string to, string subject, string html, byte[]? pdf, CancellationToken cancellationToken)
    {
        var key = secrets.Unprotect(settings.ProtectedResendApiKey); if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(settings.ResendFrom)) throw new InvalidOperationException("Resend is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var payload = new Dictionary<string, object?> { ["from"] = settings.ResendFrom, ["to"] = new[] { to }, ["subject"] = subject, ["html"] = html };
        if (pdf is not null) payload["attachments"] = new[] { new { filename = "tickets.pdf", content = Convert.ToBase64String(pdf) } };
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await http.SendAsync(request, cancellationToken); if (!response.IsSuccessStatusCode) logger.LogWarning("Resend ticket email failed with {Status}: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)); response.EnsureSuccessStatusCode();
    }

    private async Task SendSmtp(string storeId, string to, string subject, string html, byte[]? pdf, CancellationToken cancellationToken)
    {
        var emailSettings = await (await emailFactory.GetEmailSender(storeId)).GetEmailSettings(); if (emailSettings?.IsComplete() is not true) throw new InvalidOperationException("BTCPay store SMTP is not configured.");
        using var message = emailSettings.CreateMailMessage(MailboxAddress.Parse(to), subject, html, true);
        var builder = new BodyBuilder { HtmlBody = html }; if (pdf is not null) builder.Attachments.Add("tickets.pdf", pdf, ContentType.Parse("application/pdf")); message.Body = builder.ToMessageBody();
        using var smtp = await emailSettings.CreateSmtpClient(); await smtp.SendAsync(message, cancellationToken); await smtp.DisconnectAsync(true, cancellationToken);
    }
}
