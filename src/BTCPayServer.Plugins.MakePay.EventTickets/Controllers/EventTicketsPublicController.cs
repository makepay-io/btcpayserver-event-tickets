#nullable enable
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Controllers;

[Route("stores/{storeId}/events")]
public sealed class EventTicketsPublicController(StoreRepository stores, EventTicketRepository repository, UIInvoiceController invoices, TicketCodeService codes, TicketDocumentService documents, WalletPassService wallets) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Storefront(string storeId) { if (await stores.FindStore(storeId) is null) return NotFound(); return View("~/Views/EventTickets/Public/Storefront.cshtml", new EventStorefrontViewModel { StoreId = storeId, Settings = await repository.GetSettings(storeId), Events = (await repository.GetEvents(storeId)).Where(e => e.Published && e.EndsAt > DateTimeOffset.UtcNow).ToList() }); }
    [HttpGet("{eventId}")]
    public Task<IActionResult> Event(string storeId, string eventId) => ShowEvent(storeId, eventId, false);
    [HttpGet("{eventId}/pos")]
    public Task<IActionResult> Pos(string storeId, string eventId) => ShowEvent(storeId, eventId, true);
    private async Task<IActionResult> ShowEvent(string storeId, string eventId, bool posMode)
    {
        var item = await repository.GetEvent(storeId, eventId); if (item is null || !item.Published) return NotFound(); return View("~/Views/EventTickets/Public/Event.cshtml", new EventDetailViewModel { StoreId = storeId, Settings = await repository.GetSettings(storeId), Event = item, Remaining = await repository.GetRemaining(storeId, item), PosMode = posMode });
    }
    [HttpPost("{eventId}/buy")][IgnoreAntiforgeryToken]
    public async Task<IActionResult> Buy(string storeId, string eventId, string ticketTypeId, [Range(1,100)] int quantity, [EmailAddress] string email, string buyerName, bool pos, CancellationToken cancellationToken)
    {
        var store = await stores.FindStore(storeId); var item = await repository.GetEvent(storeId, eventId); var type = item?.TicketTypes.FirstOrDefault(t => t.Id == ticketTypeId && t.Active); if (store is null || item is null || type is null || !item.Published) return NotFound(); if (!new EmailAddressAttribute().IsValid(email) || string.IsNullOrWhiteSpace(buyerName)) return BadRequest("Buyer name and valid email are required.");
        var order = await repository.TryCreateOrder(storeId, item, type, quantity, email.Trim(), buyerName.Trim()); if (order is null) return Conflict("Ticket quantity is invalid or no longer available."); order.PublicBaseUrl = Request.GetAbsoluteRoot(); await repository.SaveOrder(storeId, order);
        try { var settings = await repository.GetSettings(storeId); var success = Url.ActionLink(nameof(Order), values: new { storeId, orderId = order.Id })!; var invoice = await invoices.CreateInvoiceCoreRaw(new CreateInvoiceRequest { Amount = type.Price * quantity, Currency = settings.Currency, Metadata = new InvoiceMetadata { BuyerEmail = email, BuyerName = buyerName, ItemCode = type.Id, ItemDesc = $"{item.Name} — {type.Name} × {quantity}", OrderId = order.Id, OrderUrl = Request.GetDisplayUrl() }.ToJObject(), Checkout = new InvoiceDataBase.CheckoutOptions { RedirectAutomatically = true, RedirectURL = success } }, store, Request.GetAbsoluteRoot(), [EventTicketFulfillmentService.Tag(order.Id)], cancellationToken); order.InvoiceId = invoice.Id; await repository.SaveOrder(storeId, order); return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoice.Id }); }
        catch { await repository.CancelOrder(storeId, order.Id); throw; }
    }
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> Order(string storeId, string orderId)
    {
        var order = await repository.GetOrder(storeId, orderId); if (order is null) return NotFound(); var item = await repository.GetEvent(storeId, order.EventId); var type = item?.TicketTypes.FirstOrDefault(t => t.Id == order.TicketTypeId); if (item is null || type is null) return NotFound(); var settings = await repository.GetSettings(storeId); var display = new List<DisplayTicket>();
        foreach (var id in order.TicketIds) { var ticket = await repository.GetTicket(storeId, id); var code = ticket is null ? null : codes.Unprotect(ticket.ProtectedCode); if (ticket is null || code is null) continue; display.Add(new() { Ticket = ticket, Code = code, QrDataUri = documents.QrDataUri(storeId, code), AppleWalletUrl = wallets.AppleConfigured(settings) ? Url.ActionLink(nameof(AppleWallet), values: new { storeId, orderId, ticketId = ticket.Id }) : null, GoogleWalletUrl = wallets.GoogleConfigured(settings) ? wallets.CreateGoogleSaveUrl(settings, item, type, ticket, code) : null }); }
        return View("~/Views/EventTickets/Public/Order.cshtml", new TicketOrderViewModel { Settings = settings, Event = item, TicketType = type, Order = order, Tickets = display, PdfUrl = display.Count > 0 ? Url.ActionLink(nameof(Pdf), values: new { storeId, orderId }) : null });
    }
    [HttpGet("order/{orderId}/tickets.pdf")]
    public async Task<IActionResult> Pdf(string storeId, string orderId)
    {
        var order = await repository.GetOrder(storeId, orderId); if (order is null || order.Status != TicketOrderStatus.Paid) return NotFound(); var item = await repository.GetEvent(storeId, order.EventId); var type = item?.TicketTypes.FirstOrDefault(t => t.Id == order.TicketTypeId); if (item is null || type is null) return NotFound(); var tickets = new List<IssuedTicket>(); foreach (var id in order.TicketIds) if (await repository.GetTicket(storeId, id) is { } ticket) tickets.Add(ticket); return File(documents.CreatePdf(item, type, order, tickets), "application/pdf", "tickets.pdf");
    }
    [HttpGet("order/{orderId}/wallet/apple/{ticketId}")]
    public async Task<IActionResult> AppleWallet(string storeId, string orderId, string ticketId)
    {
        var order = await repository.GetOrder(storeId, orderId); var ticket = await repository.GetTicket(storeId, ticketId); if (order is null || ticket is null || ticket.OrderId != order.Id) return NotFound(); var item = await repository.GetEvent(storeId, ticket.EventId); var type = item?.TicketTypes.FirstOrDefault(t => t.Id == ticket.TicketTypeId); var code = codes.Unprotect(ticket.ProtectedCode); if (item is null || type is null || code is null) return NotFound(); var pass = wallets.CreateApplePass(await repository.GetSettings(storeId), item, type, ticket, code); return File(pass, "application/vnd.apple.pkpass", item.Slug + ".pkpass");
    }
}
