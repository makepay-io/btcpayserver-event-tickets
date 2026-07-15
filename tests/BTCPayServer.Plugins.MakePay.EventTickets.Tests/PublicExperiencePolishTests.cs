#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Tests;

public sealed class PublicExperiencePolishTests
{
    [Fact]
    public void Payment_checkout_has_a_resilient_direct_invoice_fallback()
    {
        var source = PublicView("Payment.cshtml");
        Assert.Contains("data-invoice-link", source, StringComparison.Ordinal);
        Assert.Contains("try{if(modalOpen)", source, StringComparison.Ordinal);
        Assert.Contains("openingTimer=setTimeout", source, StringComparison.Ordinal);
        Assert.Contains("showDirectFallback", source, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_selection_explains_empty_and_sold_out_inventory()
    {
        var source = PublicView("Event.cshtml");
        Assert.Contains("Tickets are not on sale yet", source, StringComparison.Ordinal);
        Assert.Contains("This event is sold out", source, StringComparison.Ordinal);
        Assert.Contains("hasPurchasableTypes", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Storefront\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_images_are_dimensioned_and_deferred_when_below_the_fold()
    {
        var source = PublicView("Storefront.cshtml");
        Assert.Contains("loading=\"lazy\" decoding=\"async\"", source, StringComparison.Ordinal);
        Assert.Contains("width=\"1600\" height=\"1000\"", source, StringComparison.Ordinal);
        Assert.Contains("purchasableTypes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_fields_and_promotion_results_are_announced_accessibly()
    {
        var details = PublicView("Details.cshtml");
        var cart = PublicView("Cart.cshtml");
        Assert.Contains("FieldError", details, StringComparison.Ordinal);
        Assert.Contains("aria-invalid", details, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", details, StringComparison.Ordinal);
        Assert.Contains("for=\"promo-code\"", cart, StringComparison.Ordinal);
        Assert.Contains("id=\"promo-result\"", cart, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", cart, StringComparison.Ordinal);
    }

    private static string PublicView(string name) => File.ReadAllText(RepositoryFile(
        "src", "BTCPayServer.Plugins.MakePay.EventTickets", "Views", "EventTickets", "Public", name));

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }
}
