#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using BTCPayServer.Plugins.MakePay.EventTickets.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[AutoValidateAntiforgeryToken]
[Route("plugins/{storeId}/event-tickets")]
public sealed class EventTicketsAdminController(StoreRepository stores, EventTicketRepository repository, TicketCodeService secrets) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound(); ViewData.SetActivePage("EventTickets", "Event Tickets", "Event Tickets");
        return View("~/Views/EventTickets/Index.cshtml", new EventTicketsDashboardViewModel { StoreId = storeId, Settings = await repository.GetSettings(storeId), Events = await repository.GetEvents(storeId), Orders = (await repository.GetOrders(storeId)).Take(100).ToList(), Tickets = await repository.GetTickets(storeId) });
    }
    [HttpGet("events/new")][HttpGet("events/{eventId}")]
    public async Task<IActionResult> Event(string storeId, string? eventId)
    {
        var item = string.IsNullOrWhiteSpace(eventId) ? new TicketEvent { TicketTypes = [new() { Price = 25 }] } : await repository.GetEvent(storeId, eventId); if (item is null) return NotFound(); ViewData["StoreId"] = storeId; return View("~/Views/EventTickets/Event.cshtml", item);
    }
    [HttpPost("events/{eventId}")]
    public async Task<IActionResult> SaveEvent(string storeId, string eventId, TicketEvent posted)
    {
        var existing = eventId == "new" ? null : await repository.GetEvent(storeId, eventId); posted.Id = existing?.Id ?? Guid.NewGuid().ToString("N"); posted.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        posted.TicketTypes = posted.TicketTypes.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList(); foreach (var type in posted.TicketTypes) if (string.IsNullOrWhiteSpace(type.Id)) type.Id = Guid.NewGuid().ToString("N");
        if (posted.EndsAt <= posted.StartsAt) ModelState.AddModelError(nameof(posted.EndsAt), "Event end must be after its start."); if (posted.TicketTypes.Count == 0) ModelState.AddModelError(nameof(posted.TicketTypes), "Add at least one ticket type.");
        try { TimeZoneInfo.FindSystemTimeZoneById(posted.TimeZoneId); } catch { ModelState.AddModelError(nameof(posted.TimeZoneId), "Unknown time zone identifier."); }
        if (!ModelState.IsValid) { ViewData["StoreId"] = storeId; return View("~/Views/EventTickets/Event.cshtml", posted); }
        try { await repository.SaveEvent(storeId, posted); } catch (InvalidOperationException ex) { ModelState.AddModelError(nameof(posted.Slug), ex.Message); ViewData["StoreId"] = storeId; return View("~/Views/EventTickets/Event.cshtml", posted); }
        TempData.SetStatusMessageModel(new() { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Event saved." }); return RedirectToAction(nameof(Index), new { storeId });
    }
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        await SetSettingsViewData(storeId);
        return View("~/Views/EventTickets/Settings.cshtml", await repository.GetSettings(storeId));
    }
    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings(string storeId, EventTicketSettings posted, string? resendApiKey, IFormFile? appleP12, string? appleP12Password, string? googleServiceAccountJson, CancellationToken cancellationToken)
    {
        var existing = await repository.GetSettings(storeId); posted.ProtectedResendApiKey = string.IsNullOrWhiteSpace(resendApiKey) ? existing.ProtectedResendApiKey : secrets.Protect(resendApiKey);
        posted.ProtectedAppleP12 = existing.ProtectedAppleP12; posted.ProtectedAppleP12Password = existing.ProtectedAppleP12Password; posted.ProtectedGoogleServiceAccountJson = string.IsNullOrWhiteSpace(googleServiceAccountJson) ? existing.ProtectedGoogleServiceAccountJson : secrets.Protect(googleServiceAccountJson);
        if (appleP12 is not null) { if (appleP12.Length > 1024 * 1024) ModelState.AddModelError("appleP12", "Apple certificate must be smaller than 1 MB."); else { using var memory = new MemoryStream(); await appleP12.CopyToAsync(memory, cancellationToken); posted.ProtectedAppleP12 = secrets.Protect(Convert.ToBase64String(memory.ToArray())); posted.ProtectedAppleP12Password = secrets.Protect(appleP12Password ?? ""); } }
        if (!ModelState.IsValid) { await SetSettingsViewData(storeId); return View("~/Views/EventTickets/Settings.cshtml", posted); }
        await repository.SaveSettings(storeId, posted); TempData.SetStatusMessageModel(new() { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Event ticket settings saved." }); return RedirectToAction(nameof(Settings), new { storeId });
    }
    [HttpGet("scanner")]
    public IActionResult Scanner(string storeId) { ViewData["StoreId"] = storeId; return View("~/Views/EventTickets/Scanner.cshtml"); }
    [HttpPost("scanner/check-in")]
    public async Task<IActionResult> CheckIn(string storeId, [FromBody] ScannerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return BadRequest(new CheckInResult(false, "missing", null, null, null, null, "Scan or enter a ticket code.")); var result = await repository.CheckIn(storeId, request.Code, User.Identity?.Name ?? "BTCPay staff", request.Gate); return result.Success ? Ok(result) : Conflict(result);
    }
    public sealed record ScannerRequest(string Code, string? Gate);

    private async Task SetSettingsViewData(string storeId)
    {
        ViewData["StoreId"] = storeId;
        var previewEvent = (await repository.GetEvents(storeId)).FirstOrDefault(item => item.Published);
        ViewData["PreviewEventUrl"] = previewEvent is null ? null : Url.Action("Event", "EventTicketsPublic", new { storeId, eventId = previewEvent.Slug });
    }
}
