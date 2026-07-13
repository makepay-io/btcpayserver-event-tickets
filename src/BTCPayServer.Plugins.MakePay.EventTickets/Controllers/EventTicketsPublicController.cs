#nullable enable
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
public sealed class EventTicketsPublicController(
    StoreRepository stores,
    EventTicketRepository repository,
    UIInvoiceController invoices,
    TicketCodeService codes,
    TicketCheckoutService checkout,
    TicketDocumentService documents,
    WalletPassService wallets) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Storefront(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        return View("~/Views/EventTickets/Public/Storefront.cshtml", new EventStorefrontViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Events = (await repository.GetEvents(storeId)).Where(item => item.Published && item.EndsAt > DateTimeOffset.UtcNow).ToList()
        });
    }

    [HttpGet("{eventId}")]
    public Task<IActionResult> Event(string storeId, string eventId) => ShowEvent(storeId, eventId, false);

    [HttpGet("{eventId}/pos")]
    public Task<IActionResult> Pos(string storeId, string eventId) => ShowEvent(storeId, eventId, true);

    private async Task<IActionResult> ShowEvent(string storeId, string eventId, bool posMode)
    {
        var item = await repository.GetEvent(storeId, eventId);
        if (item is null || !item.Published) return NotFound();
        return View("~/Views/EventTickets/Public/Event.cshtml", new EventDetailViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Event = item,
            Remaining = await repository.GetRemaining(storeId, item),
            PosMode = posMode
        });
    }

    [HttpPost("{eventId}/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartCheckout(string storeId, string eventId, TicketSelectionInput input)
    {
        var store = await stores.FindStore(storeId);
        var item = await repository.GetEvent(storeId, eventId);
        if (store is null || item is null || !item.Published) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var lines = TicketCheckoutService.BuildLines(item, input.Quantities ?? new Dictionary<string, int>());
        if (lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one available ticket.");
            return await ShowEvent(storeId, eventId, input.Pos);
        }

        var order = await repository.TryCreateOrder(storeId, item, settings, lines);
        if (order is null)
        {
            ModelState.AddModelError(string.Empty, "The selected quantity is no longer available. Please choose again.");
            return await ShowEvent(storeId, eventId, input.Pos);
        }

        order.PublicBaseUrl = Request.GetAbsoluteRoot();
        order.PosMode = input.Pos;
        var accessToken = checkout.CreateAccessToken(order);
        await repository.SaveOrder(storeId, order);
        return RedirectToAction(nameof(Cart), new { storeId, eventId = item.Slug, orderId = order.Id, accessToken });
    }

    [HttpGet("{eventId}/checkout/{orderId}/cart")]
    public async Task<IActionResult> Cart(string storeId, string eventId, string orderId, string? accessToken, string? promo)
    {
        var page = await BuildCheckoutPage(storeId, eventId, orderId, accessToken, 2);
        if (page is null) return NotFound();
        page = new TicketCheckoutPageViewModel
        {
            StoreId = page.StoreId,
            Settings = page.Settings,
            Event = page.Event,
            Order = page.Order,
            Lines = page.Lines,
            AccessToken = page.AccessToken,
            Step = 2,
            PromoMessage = promo == "applied" ? "Promotion applied." : promo == "invalid" ? "That promotion code is not valid." : null
        };
        return View("~/Views/EventTickets/Public/Cart.cshtml", page);
    }

    [HttpPost("{eventId}/checkout/{orderId}/promotion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyPromotion(string storeId, string eventId, string orderId, string? accessToken, string? promoCode)
    {
        var order = await repository.GetOrder(storeId, orderId);
        var item = await repository.GetEvent(storeId, eventId);
        if (order is null || item is null || order.EventId != item.Id || !checkout.CanAccess(order, accessToken)) return NotFound();
        if (order.Status != TicketOrderStatus.Pending || order.ReservationExpiresAt <= DateTimeOffset.UtcNow) return RedirectToAction(nameof(Cart), new { storeId, eventId = item.Slug, orderId, accessToken });
        var applied = TicketCheckoutService.ApplyPromo(order, await repository.GetSettings(storeId), promoCode);
        await repository.SaveOrder(storeId, order);
        return RedirectToAction(nameof(Cart), new { storeId, eventId = item.Slug, orderId, accessToken, promo = applied ? "applied" : "invalid" });
    }

    [HttpGet("{eventId}/checkout/{orderId}/details")]
    public async Task<IActionResult> Details(string storeId, string eventId, string orderId, string? accessToken)
    {
        var page = await BuildCheckoutPage(storeId, eventId, orderId, accessToken, 3);
        if (page is null) return NotFound();
        var input = new TicketCheckoutInput
        {
            BuyerFirstName = page.Order.BuyerFirstName,
            BuyerLastName = page.Order.BuyerLastName,
            BuyerEmail = page.Order.BuyerEmail,
            BuyerPhone = page.Order.BuyerPhone,
            BuyerCountry = page.Order.BuyerCountry,
            BuyerCompany = page.Order.BuyerCompany,
            AcceptTerms = page.Order.TermsAcceptedAt is not null,
            Attendees = page.Order.Attendees.Count > 0
                ? page.Order.Attendees.Select(ToInput).ToList()
                : page.Lines.SelectMany(line => Enumerable.Range(0, line.Line.Quantity).Select(_ => new TicketAttendeeInput { TicketTypeId = line.TicketType.Id })).ToList()
        };
        return View("~/Views/EventTickets/Public/Details.cshtml", WithInput(page, input));
    }

    [HttpPost("{eventId}/checkout/{orderId}/payment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayment(string storeId, string eventId, string orderId, string? accessToken, TicketCheckoutInput input, CancellationToken cancellationToken)
    {
        var page = await BuildCheckoutPage(storeId, eventId, orderId, accessToken, 3);
        if (page is null) return NotFound();
        if (page.Order.Status != TicketOrderStatus.Pending || page.Order.ReservationExpiresAt <= DateTimeOffset.UtcNow)
        {
            await repository.CancelOrder(storeId, orderId);
            return View("~/Views/EventTickets/Public/Details.cshtml", WithInput(page, input));
        }

        if (!input.AcceptTerms) ModelState.AddModelError(nameof(input.AcceptTerms), "Accept the terms and privacy notice to continue.");
        if (page.Settings.RequirePhone && string.IsNullOrWhiteSpace(input.BuyerPhone)) ModelState.AddModelError(nameof(input.BuyerPhone), "Phone number is required.");
        if (page.Settings.RequireCountry && string.IsNullOrWhiteSpace(input.BuyerCountry)) ModelState.AddModelError(nameof(input.BuyerCountry), "Country is required.");
        var expectedTypes = page.Lines.SelectMany(line => Enumerable.Repeat(line.TicketType.Id, line.Line.Quantity)).ToList();
        if (input.Attendees.Count != expectedTypes.Count) ModelState.AddModelError(nameof(input.Attendees), "Attendee details are required for every ticket.");
        if (!ModelState.IsValid) return View("~/Views/EventTickets/Public/Details.cshtml", WithInput(page, input));

        for (var index = 0; index < input.Attendees.Count; index++) input.Attendees[index].TicketTypeId = expectedTypes[index];
        var order = page.Order;
        order.BuyerFirstName = input.BuyerFirstName.Trim();
        order.BuyerLastName = input.BuyerLastName.Trim();
        order.BuyerName = (order.BuyerFirstName + " " + order.BuyerLastName).Trim();
        order.BuyerEmail = input.BuyerEmail.Trim();
        order.BuyerPhone = input.BuyerPhone?.Trim() ?? "";
        order.BuyerCountry = input.BuyerCountry?.Trim() ?? "";
        order.BuyerCompany = input.BuyerCompany?.Trim() ?? "";
        order.TermsAcceptedAt = DateTimeOffset.UtcNow;
        order.Attendees = input.Attendees.Select(attendee => new TicketAttendee
        {
            TicketTypeId = attendee.TicketTypeId,
            FirstName = attendee.FirstName.Trim(),
            LastName = attendee.LastName.Trim(),
            Nickname = attendee.Nickname?.Trim() ?? "",
            Email = attendee.Email.Trim(),
            Phone = attendee.Phone?.Trim() ?? "",
            Country = attendee.Country?.Trim() ?? "",
            Company = attendee.Company?.Trim() ?? ""
        }).ToList();
        await repository.SaveOrder(storeId, order);

        if (string.IsNullOrWhiteSpace(order.InvoiceId))
        {
            var store = await stores.FindStore(storeId);
            if (store is null) return NotFound();
            try
            {
                var successUrl = Url.ActionLink(nameof(Order), values: new { storeId, orderId = order.Id, accessToken })!;
                var invoice = await invoices.CreateInvoiceCoreRaw(new CreateInvoiceRequest
                {
                    Amount = order.Total,
                    Currency = order.Currency,
                    Metadata = new InvoiceMetadata
                    {
                        BuyerEmail = order.BuyerEmail,
                        BuyerName = order.BuyerName,
                        ItemCode = page.Event.Id,
                        ItemDesc = $"{page.Event.Name} — {order.Quantity} ticket{(order.Quantity == 1 ? "" : "s")}",
                        OrderId = order.Id,
                        OrderUrl = successUrl
                    }.ToJObject(),
                    Checkout = new InvoiceDataBase.CheckoutOptions
                    {
                        Expiration = TimeSpan.FromMinutes(page.Settings.CheckoutMinutes),
                        RedirectAutomatically = true,
                        RedirectURL = successUrl
                    }
                }, store, Request.GetAbsoluteRoot(), [EventTicketFulfillmentService.Tag(order.Id)], cancellationToken);
                order.InvoiceId = invoice.Id;
                await repository.SaveOrder(storeId, order);
            }
            catch
            {
                await repository.CancelOrder(storeId, order.Id);
                throw;
            }
        }

        return RedirectToAction(nameof(Payment), new { storeId, eventId = page.Event.Slug, orderId = order.Id, accessToken });
    }

    [HttpGet("{eventId}/checkout/{orderId}/payment")]
    public async Task<IActionResult> Payment(string storeId, string eventId, string orderId, string? accessToken)
    {
        var page = await BuildCheckoutPage(storeId, eventId, orderId, accessToken, 4);
        if (page is null || string.IsNullOrWhiteSpace(page.Order.InvoiceId)) return NotFound();
        if (page.Order.Status == TicketOrderStatus.Paid) return RedirectToAction(nameof(Order), new { storeId, orderId, accessToken });
        return View("~/Views/EventTickets/Public/Payment.cshtml", page);
    }

    [HttpGet("order/{orderId}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> OrderStatus(string storeId, string orderId, string? accessToken)
    {
        var order = await repository.GetOrder(storeId, orderId);
        if (order is null || !checkout.CanAccess(order, accessToken)) return NotFound();
        return order.Status switch
        {
            TicketOrderStatus.Paid => Ok(new TicketPaymentStatus("paid", Url.ActionLink(nameof(Order), values: new { storeId, orderId, accessToken }), "Payment confirmed.")),
            TicketOrderStatus.Cancelled => Ok(new TicketPaymentStatus("cancelled", null, "This payment session expired or was cancelled.")),
            _ => Ok(new TicketPaymentStatus("pending", null, "Waiting for payment confirmation."))
        };
    }

    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> Order(string storeId, string orderId, string? accessToken)
    {
        var order = await repository.GetOrder(storeId, orderId);
        if (order is null || !checkout.CanAccess(order, accessToken)) return NotFound();
        var item = await repository.GetEvent(storeId, order.EventId);
        if (item is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var display = new List<DisplayTicket>();
        foreach (var id in order.TicketIds)
        {
            var ticket = await repository.GetTicket(storeId, id);
            var code = ticket is null ? null : codes.Unprotect(ticket.ProtectedCode);
            var type = ticket is null ? null : item.TicketTypes.FirstOrDefault(ticketType => ticketType.Id == ticket.TicketTypeId);
            if (ticket is null || code is null || type is null) continue;
            display.Add(new DisplayTicket
            {
                Ticket = ticket,
                TicketType = type,
                Code = code,
                QrDataUri = documents.QrDataUri(storeId, code),
                AppleWalletUrl = wallets.AppleConfigured(settings) ? Url.ActionLink(nameof(AppleWallet), values: new { storeId, orderId, ticketId = ticket.Id, accessToken }) : null,
                GoogleWalletUrl = wallets.GoogleConfigured(settings) ? wallets.CreateGoogleSaveUrl(settings, item, type, ticket, code) : null
            });
        }
        return View("~/Views/EventTickets/Public/Order.cshtml", new TicketOrderViewModel
        {
            Settings = settings,
            Event = item,
            Order = order,
            Lines = TicketCheckoutService.ResolveLineViewModels(order, item),
            Tickets = display,
            AccessToken = accessToken ?? string.Empty,
            PdfUrl = display.Count > 0 ? Url.ActionLink(nameof(Pdf), values: new { storeId, orderId, accessToken }) : null
        });
    }

    [HttpGet("order/{orderId}/tickets.pdf")]
    public async Task<IActionResult> Pdf(string storeId, string orderId, string? accessToken)
    {
        var order = await repository.GetOrder(storeId, orderId);
        if (order is null || order.Status != TicketOrderStatus.Paid || !checkout.CanAccess(order, accessToken)) return NotFound();
        var item = await repository.GetEvent(storeId, order.EventId);
        if (item is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var tickets = new List<IssuedTicket>();
        foreach (var id in order.TicketIds) if (await repository.GetTicket(storeId, id) is { } ticket) tickets.Add(ticket);
        return File(documents.CreatePdf(item, order, tickets, settings), "application/pdf", item.Slug + "-tickets.pdf");
    }

    [HttpGet("order/{orderId}/wallet/apple/{ticketId}")]
    public async Task<IActionResult> AppleWallet(string storeId, string orderId, string ticketId, string? accessToken)
    {
        var order = await repository.GetOrder(storeId, orderId);
        var ticket = await repository.GetTicket(storeId, ticketId);
        if (order is null || ticket is null || ticket.OrderId != order.Id || !checkout.CanAccess(order, accessToken)) return NotFound();
        var item = await repository.GetEvent(storeId, ticket.EventId);
        var type = item?.TicketTypes.FirstOrDefault(ticketType => ticketType.Id == ticket.TicketTypeId);
        var code = codes.Unprotect(ticket.ProtectedCode);
        if (item is null || type is null || code is null) return NotFound();
        var pass = wallets.CreateApplePass(await repository.GetSettings(storeId), item, type, ticket, code);
        return File(pass, "application/vnd.apple.pkpass", item.Slug + ".pkpass");
    }

    private async Task<TicketCheckoutPageViewModel?> BuildCheckoutPage(string storeId, string eventId, string orderId, string? accessToken, int step)
    {
        var order = await repository.GetOrder(storeId, orderId);
        var item = await repository.GetEvent(storeId, eventId);
        if (order is null || item is null || order.EventId != item.Id || !checkout.CanAccess(order, accessToken)) return null;
        if (order.Status == TicketOrderStatus.Pending && order.ReservationExpiresAt <= DateTimeOffset.UtcNow)
        {
            await repository.CancelOrder(storeId, order.Id);
            order.Status = TicketOrderStatus.Cancelled;
        }
        return new TicketCheckoutPageViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Event = item,
            Order = order,
            Lines = TicketCheckoutService.ResolveLineViewModels(order, item),
            AccessToken = accessToken ?? string.Empty,
            Step = step
        };
    }

    private static TicketCheckoutPageViewModel WithInput(TicketCheckoutPageViewModel page, TicketCheckoutInput input) => new()
    {
        StoreId = page.StoreId,
        Settings = page.Settings,
        Event = page.Event,
        Order = page.Order,
        Lines = page.Lines,
        AccessToken = page.AccessToken,
        Step = 3,
        Input = input
    };

    private static TicketAttendeeInput ToInput(TicketAttendee attendee) => new()
    {
        TicketTypeId = attendee.TicketTypeId,
        FirstName = attendee.FirstName,
        LastName = attendee.LastName,
        Nickname = attendee.Nickname,
        Email = attendee.Email,
        Phone = attendee.Phone,
        Country = attendee.Country,
        Company = attendee.Company
    };
}
