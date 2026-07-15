#nullable enable
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Controllers;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TicketNoStoreAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["Cache-Control"] = "no-store, no-cache, private, max-age=0, must-revalidate";
        headers["Pragma"] = "no-cache";
        headers["Expires"] = "0";
    }
}

public abstract class EventTicketsPublicControllerBase(
    StoreRepository stores,
    EventTicketRepository repository,
    InvoiceRepository invoiceRepository,
    UIInvoiceController invoices,
    TicketCodeService codes,
    TicketCheckoutService checkout,
    TicketDocumentService documents,
    WalletPassService wallets,
    EventTicketsAppService eventApps) : Controller
{
    protected abstract bool CleanUrls { get; }
    protected EventTicketsAppService EventApps { get; } = eventApps;

    [HttpGet("")]
    public async Task<IActionResult> Storefront(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var now = DateTimeOffset.UtcNow;
        var events = (await repository.GetEvents(storeId)).Where(item => TicketEventSalePolicy.CanStartCheckout(item, now)).ToList();
        return View("~/Views/EventTickets/Public/Storefront.cshtml", new EventStorefrontViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Events = events,
            Remaining = await repository.GetRemaining(storeId, events),
            CleanUrls = CleanUrls
        });
    }

    [HttpGet("{eventId}")]
    public Task<IActionResult> Event(string storeId, string eventId) => ShowEvent(storeId, eventId, false);

    [HttpGet("{eventId}/pos")]
    public Task<IActionResult> Pos(string storeId, string eventId) => ShowEvent(storeId, eventId, true);

    [HttpGet("{eventId}/scanner")]
    [TicketNoStore]
    public async Task<IActionResult> Scanner(string storeId, string eventId, string? scannerToken)
    {
        PrepareScannerResponse();
        var item = await repository.GetEvent(storeId, eventId);
        if (item is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(scannerToken))
        {
            if (!TicketCodeService.CanAccessScanner(item, scannerToken)) return NotFound();
            SetScannerSessionCookie(storeId, item.Slug, scannerToken);
            return RedirectPublic(nameof(Scanner), new { storeId, eventId = item.Slug });
        }

        var scannerSession = Request.Cookies[TicketCodeService.ScannerSessionCookieName];
        if (!TicketCodeService.CanAccessScanner(item, scannerSession))
            return NotFound();
        SetScannerSessionCookie(storeId, item.Slug, scannerSession!);

        var settings = await repository.GetSettings(storeId);
        return View("~/Views/EventTickets/Public/Scanner.cshtml", new EventScannerViewModel
        {
            StoreId = storeId,
            Settings = settings,
            Event = item,
            CheckInUrl = PublicAction(nameof(ScannerCheckIn), new { storeId, eventId = item.Slug }) ?? "",
            EventUrl = PublicAction(nameof(Event), new { storeId, eventId = item.Slug }) ?? "",
            CleanUrls = CleanUrls
        });
    }

    [HttpPost("{eventId}/scanner/check-in")]
    [ValidateAntiForgeryToken]
    [TicketNoStore]
    public async Task<IActionResult> ScannerCheckIn(string storeId, string eventId, [FromBody] ScannerCheckInRequest? request)
    {
        PrepareScannerResponse();
        var item = await repository.GetEvent(storeId, eventId);
        if (item is null) return NotFound();
        var scannerSession = Request.Cookies[TicketCodeService.ScannerSessionCookieName];
        if (!TicketCodeService.CanAccessScanner(item, scannerSession))
            return Unauthorized(new CheckInResult(false, "unauthorized", null, null, null, null, "Scanner access expired. Reopen the event scanner link."));
        SetScannerSessionCookie(storeId, item.Slug, scannerSession!);
        if (request is null || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new CheckInResult(false, "missing", null, null, null, null, "Scan or enter a ticket code."));
        if (request.Code.Length > 512 || request.Gate?.Length > 80)
            return BadRequest(new CheckInResult(false, "invalid", null, null, null, null, "The scanner input is too long."));

        var result = await repository.CheckIn(storeId, item, request.Code, "Event staff scanner", request.Gate);
        return result.Success ? Ok(result) : Conflict(result);
    }

    private async Task<IActionResult> ShowEvent(string storeId, string eventId, bool posMode)
    {
        var item = await repository.GetEvent(storeId, eventId);
        if (item is null || !TicketEventSalePolicy.CanStartCheckout(item, DateTimeOffset.UtcNow)) return NotFound();
        return View("~/Views/EventTickets/Public/Event.cshtml", new EventDetailViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Event = item,
            Remaining = await repository.GetRemaining(storeId, item),
            PosMode = posMode,
            CleanUrls = CleanUrls
        });
    }

    [HttpPost("{eventId}/checkout")]
    [ValidateAntiForgeryToken]
    [TicketNoStore]
    public async Task<IActionResult> StartCheckout(string storeId, string eventId, TicketSelectionInput input)
    {
        var store = await stores.FindStore(storeId);
        var item = await repository.GetEvent(storeId, eventId);
        if (store is null || item is null || !TicketEventSalePolicy.CanStartCheckout(item, DateTimeOffset.UtcNow)) return NotFound();
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

        TicketPublicUrl.CaptureOrderOrigin(order, CleanUrls, Request.IsOnion(),
            await EventApps.GetMappedBaseUrl(storeId), Request.GetAbsoluteRoot());
        order.PosMode = input.Pos;
        var accessToken = checkout.CreateAccessToken(order);
        await repository.SaveOrder(storeId, order);
        return RedirectPublic(nameof(Cart), new { storeId, eventId = item.Slug, orderId = order.Id, accessToken });
    }

    [HttpPost("{eventId}/checkout/{orderId}/rebuy")]
    [ValidateAntiForgeryToken]
    [TicketNoStore]
    public async Task<IActionResult> Rebuy(string storeId, string eventId, string orderId, string? accessToken)
    {
        var previousOrder = await repository.GetOrder(storeId, orderId);
        var item = await repository.GetEvent(storeId, eventId);
        if (previousOrder is null || item is null || previousOrder.EventId != item.Id ||
            !checkout.CanAccess(previousOrder, accessToken)) return NotFound();

        if (previousOrder.Status == TicketOrderStatus.Paid)
            return RedirectPublic(nameof(Order), new { storeId, orderId, accessToken });

        if (previousOrder.Status == TicketOrderStatus.Pending)
        {
            if (!string.IsNullOrWhiteSpace(previousOrder.InvoiceId))
                return RedirectPublic(nameof(Payment), new { storeId, eventId = item.Slug, orderId, accessToken });
            if (!TicketReservationPolicy.CanExpire(previousOrder, DateTimeOffset.UtcNow))
                return RedirectPublic(nameof(Details), new { storeId, eventId = item.Slug, orderId, accessToken });

            await repository.CancelOrder(storeId, previousOrder.Id);
            previousOrder.Status = TicketOrderStatus.Cancelled;
        }

        if (!TicketEventSalePolicy.CanStartCheckout(item, DateTimeOffset.UtcNow))
            return RedirectPublic(nameof(Storefront), new { storeId });

        var settings = await repository.GetSettings(storeId);
        var lines = TicketCheckoutService.BuildRebuyLines(previousOrder, item);
        if (previousOrder.Status != TicketOrderStatus.Cancelled || lines.Count == 0)
            return RedirectPublic(nameof(Event), new { storeId, eventId = item.Slug });

        var order = await repository.TryCreateOrder(storeId, item, settings, lines);
        if (order is null)
            return RedirectPublic(nameof(Event), new { storeId, eventId = item.Slug });

        TicketPublicUrl.CaptureOrderOrigin(order, CleanUrls, Request.IsOnion(),
            await EventApps.GetMappedBaseUrl(storeId), Request.GetAbsoluteRoot());
        order.PosMode = previousOrder.PosMode;
        if (!string.IsNullOrWhiteSpace(previousOrder.PromoCode))
            TicketCheckoutService.ApplyPromo(order, settings, previousOrder.PromoCode);
        var newAccessToken = checkout.CreateAccessToken(order);
        await repository.SaveOrder(storeId, order);
        return RedirectPublic(nameof(Details), new
        {
            storeId,
            eventId = item.Slug,
            orderId = order.Id,
            accessToken = newAccessToken
        });
    }

    [HttpGet("{eventId}/checkout/{orderId}/cart")]
    [TicketNoStore]
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
            PromoMessage = promo == "applied" ? "Promotion applied." : promo == "invalid" ? "That promotion code is not valid." : null,
            CleanUrls = CleanUrls
        };
        return View("~/Views/EventTickets/Public/Cart.cshtml", page);
    }

    [HttpPost("{eventId}/checkout/{orderId}/promotion")]
    [ValidateAntiForgeryToken]
    [TicketNoStore]
    public async Task<IActionResult> ApplyPromotion(string storeId, string eventId, string orderId, string? accessToken, string? promoCode)
    {
        var order = await repository.GetOrder(storeId, orderId);
        var item = await repository.GetEvent(storeId, eventId);
        if (order is null || item is null || order.EventId != item.Id || !checkout.CanAccess(order, accessToken)) return NotFound();
        if (order.Status != TicketOrderStatus.Pending || TicketReservationPolicy.CanExpire(order, DateTimeOffset.UtcNow)) return RedirectPublic(nameof(Event), new { storeId, eventId = item.Slug });
        var applied = TicketCheckoutService.ApplyPromo(order, await repository.GetSettings(storeId), promoCode);
        await repository.SaveOrder(storeId, order);
        return RedirectPublic(nameof(Cart), new { storeId, eventId = item.Slug, orderId, accessToken, promo = applied ? "applied" : "invalid" });
    }

    [HttpGet("{eventId}/checkout/{orderId}/details")]
    [TicketNoStore]
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
    [TicketNoStore]
    public async Task<IActionResult> CreatePayment(string storeId, string eventId, string orderId, string? accessToken, TicketCheckoutInput input, CancellationToken cancellationToken)
    {
        var page = await BuildCheckoutPage(storeId, eventId, orderId, accessToken, 3);
        if (page is null) return NotFound();
        if (page.Order.Status != TicketOrderStatus.Pending || TicketReservationPolicy.CanExpire(page.Order, DateTimeOffset.UtcNow))
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
                // The invoice redirect honors the persisted route/origin intent.
                // This keeps onion checkouts on their onion origin even when the
                // same store also has a native clearnet App mapping.
                var successUrl = TicketPublicUrl.OrderUrl(order, accessToken, await EventApps.GetMappedBaseUrl(storeId));
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

        return RedirectPublic(nameof(Payment), new { storeId, eventId = page.Event.Slug, orderId = order.Id, accessToken });
    }

    [HttpGet("{eventId}/checkout/{orderId}/payment")]
    [TicketNoStore]
    public async Task<IActionResult> Payment(string storeId, string eventId, string orderId, string? accessToken)
    {
        var page = await BuildCheckoutPage(storeId, eventId, orderId, accessToken, 4);
        if (page is null || string.IsNullOrWhiteSpace(page.Order.InvoiceId)) return NotFound();
        if (page.Order.Status is TicketOrderStatus.Paid or TicketOrderStatus.Cancelled)
            return RedirectPublic(nameof(Order), new { storeId, orderId, accessToken });
        return View("~/Views/EventTickets/Public/Payment.cshtml", page);
    }

    [HttpGet("order/{orderId}/status")]
    [TicketNoStore]
    public async Task<IActionResult> OrderStatus(string storeId, string orderId, string? accessToken)
    {
        var order = await repository.GetOrder(storeId, orderId);
        if (order is null || !checkout.CanAccess(order, accessToken)) return NotFound();
        if (order.Status == TicketOrderStatus.Paid)
            return Ok(new TicketPaymentStatus("paid", PublicActionLink(nameof(Order), new { storeId, orderId, accessToken }), "Payment confirmed."));
        var item = await repository.GetEvent(storeId, order.EventId);
        var restartUrl = item is null || !TicketEventSalePolicy.CanStartCheckout(item, DateTimeOffset.UtcNow)
            ? PublicAction(nameof(Storefront), new { storeId })
            : PublicAction(nameof(Event), new { storeId, eventId = item.Slug });
        if (order.Status == TicketOrderStatus.Cancelled)
            return Ok(new TicketPaymentStatus("cancelled", restartUrl, "This invoice can no longer be paid. Choose your tickets again."));
        if (!string.IsNullOrWhiteSpace(order.InvoiceId))
        {
            var invoice = await invoiceRepository.GetInvoice(order.InvoiceId);
            if (invoice is not null)
            {
                var hasPayment = invoice.GetPayments(false).Any();
                if (invoice.Status == InvoiceStatus.Settled) return Ok(new TicketPaymentStatus("processing", null, "Payment settled. Preparing your tickets."));
                if (invoice.Status == InvoiceStatus.Processing) return Ok(new TicketPaymentStatus("processing", null, "Payment detected. Waiting for blockchain confirmations."));
                if (invoice.Status == InvoiceStatus.Expired && hasPayment) return Ok(new TicketPaymentStatus("partial", null, "Payment detected. BTCPay is waiting for the remaining amount or confirmation."));
                if (invoice.Status == InvoiceStatus.New && hasPayment) return Ok(new TicketPaymentStatus("partial", null, "Payment detected. Waiting for BTCPay to update the invoice."));
                if (invoice.Status is InvoiceStatus.Expired or InvoiceStatus.Invalid)
                {
                    await repository.CancelOrder(storeId, orderId);
                    return Ok(new TicketPaymentStatus("cancelled", restartUrl, "This invoice can no longer be paid. Choose your tickets again."));
                }
            }
        }
        return Ok(new TicketPaymentStatus("pending", null, "Waiting for payment confirmation."));
    }

    [HttpGet("order/{orderId}")]
    [TicketNoStore]
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
                AppleWalletUrl = wallets.AppleConfigured(settings) ? PublicActionLink(nameof(AppleWallet), new { storeId, orderId, ticketId = ticket.Id, accessToken }) : null,
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
            PdfUrl = display.Count > 0 ? PublicActionLink(nameof(Pdf), new { storeId, orderId, accessToken }) : null,
            CleanUrls = CleanUrls
        });
    }

    [HttpGet("order/{orderId}/tickets.pdf")]
    [TicketNoStore]
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
    [TicketNoStore]
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
        if (TicketReservationPolicy.CanExpire(order, DateTimeOffset.UtcNow))
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
            Step = step,
            CleanUrls = CleanUrls
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
        Input = input,
        CleanUrls = page.CleanUrls
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

    private RouteValueDictionary PublicValues(object values)
    {
        var routeValues = new RouteValueDictionary(values);
        if (CleanUrls) routeValues.Remove("storeId");
        return routeValues;
    }

    private RedirectToActionResult RedirectPublic(string action, object values) =>
        RedirectToAction(action, CleanUrls ? TicketPublicUrl.CleanController : TicketPublicUrl.LegacyController,
            PublicValues(values));

    private string? PublicAction(string action, object values) =>
        Url.Action(action, CleanUrls ? TicketPublicUrl.CleanController : TicketPublicUrl.LegacyController,
            PublicValues(values));

    private string? PublicActionLink(string action, object values) =>
        Url.ActionLink(action, CleanUrls ? TicketPublicUrl.CleanController : TicketPublicUrl.LegacyController,
            PublicValues(values));

    private void PrepareScannerResponse()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["Permissions-Policy"] = "camera=(self)";
    }

    private void SetScannerSessionCookie(string storeId, string eventSlug, string token)
    {
        var path = PublicAction(nameof(Scanner), new { storeId, eventId = eventSlug })
                   ?? Request.PathBase.Add(Request.Path).Value
                   ?? "/";
        Response.Cookies.Append(TicketCodeService.ScannerSessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            MaxAge = TimeSpan.FromHours(12),
            Path = path
        });
    }
}

[Route("stores/{storeId}/events")]
public sealed class EventTicketsPublicController(
    StoreRepository stores,
    EventTicketRepository repository,
    InvoiceRepository invoiceRepository,
    UIInvoiceController invoices,
    TicketCodeService codes,
    TicketCheckoutService checkout,
    TicketDocumentService documents,
    WalletPassService wallets,
    EventTicketsAppService eventApps) : EventTicketsPublicControllerBase(stores, repository, invoiceRepository, invoices, codes, checkout, documents, wallets, eventApps)
{
    protected override bool CleanUrls => false;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if ((HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method)) && !Request.IsOnion() &&
            context.ActionArguments.TryGetValue("storeId", out var value) && value is string storeId)
        {
            var (app, domain) = await EventApps.MappingForStore(storeId);
            if (app is not null) HttpContext.SetAppData(app);
            var redirect = domain is null
                ? null
                : TicketPublicUrl.CleanUrlFromLegacy(await EventApps.GetMappedBaseUrl(storeId), storeId,
                    Request.PathBase, Request.Path, Request.QueryString);
            if (redirect is not null)
            {
                context.Result = new RedirectResult(redirect, permanent: true, preserveMethod: true);
                return;
            }
        }
        await base.OnActionExecutionAsync(context, next);
    }
}

[Route("events")]
[DomainMappingConstraint(EventTicketsAppType.AppType)]
public sealed class CleanEventTicketsPublicController(
    StoreRepository stores,
    EventTicketRepository repository,
    InvoiceRepository invoiceRepository,
    UIInvoiceController invoices,
    TicketCodeService codes,
    TicketCheckoutService checkout,
    TicketDocumentService documents,
    WalletPassService wallets,
    EventTicketsAppService eventApps) : EventTicketsPublicControllerBase(stores, repository, invoiceRepository, invoices, codes, checkout, documents, wallets, eventApps)
{
    protected override bool CleanUrls => true;

    [HttpGet("/")]
    public IActionResult Root() => Redirect(Request.PathBase.Add(new PathString("/events")));

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var appId = RouteData.Values["appId"] as string;
        var app = appId is null ? null : await EventApps.Get(appId);
        if (app is null)
        {
            context.Result = NotFound();
            return;
        }
        // The store is always derived from BTCPay's mapped AppData. A query,
        // form, or route value named storeId can never select another store.
        TicketPublicUrl.BindMappedStore(app.StoreDataId, context.ActionArguments, context.RouteData.Values, ModelState);
        HttpContext.SetAppData(app);
        await base.OnActionExecutionAsync(context, next);
    }
}
