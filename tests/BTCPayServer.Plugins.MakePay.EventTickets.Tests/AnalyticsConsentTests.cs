#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Tests;

public sealed class AnalyticsConsentTests
{
    private static readonly string Analytics = Source("Views", "EventTickets", "Public", "_Analytics.cshtml");
    private static readonly string Runtime = Analytics[(Analytics.IndexOf("<script>", StringComparison.Ordinal) + "<script>".Length)..];

    [Fact]
    public void Consent_pending_events_are_bounded_queued_and_flushed_after_collection_starts()
    {
        Assert.Contains("const queuedEvents=[]", Runtime, StringComparison.Ordinal);
        Assert.Contains("const maxQueuedEvents=100", Runtime, StringComparison.Ordinal);
        Assert.Contains("const consentPending=()=>providerConfigured&&!dnt&&config.requireConsent", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(!consentPending())return false", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(queuedEvents.length>=maxQueuedEvents)", Runtime, StringComparison.Ordinal);
        Assert.Contains("queuedEvents.push({name,payload,key})", Runtime, StringComparison.Ordinal);
        Assert.Contains("const pending=queuedEvents.splice(0,queuedEvents.length)", Runtime, StringComparison.Ordinal);
        Assert.Contains("loadExternal();flushQueue()", Runtime, StringComparison.Ordinal);

        var accept = Section("accept.addEventListener", "decline.addEventListener");
        Assert.True(accept.IndexOf("enableCollection()", StringComparison.Ordinal) <
                    accept.IndexOf("dispatchEvent(new CustomEvent(consentGrantedEvent))", StringComparison.Ordinal),
            "The queue must flush before legacy consent listeners retry their event calls.");
    }

    [Fact]
    public void Queued_once_events_are_replayed_exactly_once_and_purchase_is_local_deduped()
    {
        Assert.Contains("const queuedOnceKeys=new Set()", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(key&&queuedOnceKeys.has(key.value))return false", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(item.key&&hasEmitted(item.key))continue", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(item.key)markEmitted(item.key);", Runtime, StringComparison.Ordinal);
        Assert.Contains("const hasEmitted=key=>emittedOnceKeys.has(key.value)||storage.get(key.kind,key.value)==='1'", Runtime, StringComparison.Ordinal);
        Assert.Contains("const purchaseOnce=(transactionId,payload)=>trackOnce('purchase',transactionId,payload,'local')", Runtime, StringComparison.Ordinal);
        Assert.Contains("const oncePrefix=`makepay:analytics:once:v1:event_tickets:${config.storeId}`", Runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("once:v1:event_tickets:${config.storeId}:${config.provider}", Runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Queued_payloads_are_allowlisted_and_never_persist_raw_identifiers_or_customer_fields()
    {
        Assert.Contains("const sanitizePayload=payload=>({ecommerce:sanitizeEcommerce", Runtime, StringComparison.Ordinal);
        Assert.Contains("source.items.slice(0,100).map(sanitizeItem)", Runtime, StringComparison.Ordinal);
        Assert.Contains("/^mpt_[a-f0-9]{64}$/.test(transactionId)", Runtime, StringComparison.Ordinal);
        Assert.Contains("const opaqueOnceId=value=>", Runtime, StringComparison.Ordinal);
        Assert.Contains("return `opaque_${", Runtime, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "buyer_email", "buyer_name", "first_name", "last_name", "phone_number",
                     "attendee_details", "payment_address", "access_token", "invoice_id"
                 })
        {
            Assert.DoesNotContain($"'{forbidden}'", Runtime, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"\"{forbidden}\"", Runtime, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Disabled_mode_dnt_gtm_and_ga4_keep_their_distinct_privacy_contracts()
    {
        Assert.Contains("let collectionAllowed=!providerConfigured&&!dnt", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(!providerConfigured){hideConsent();if(!dnt){collectionAllowed=true;pushContext()}return}", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(dnt){collectionAllowed=false;clearQueue();consentUpdate(false);consent.hidden=true;preferences.hidden=true;return}", Runtime, StringComparison.Ordinal);
        Assert.Contains("config.provider==='gtm'", Runtime, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/gtm.js", Runtime, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/gtag/js", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(config.provider==='ga4'&&externalActive)sendDirect", Runtime, StringComparison.Ordinal);
        Assert.Contains("analytics_storage:!dnt&&(!config.requireConsent||consentChoice==='granted')?'granted':'denied'", Runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Consent_dialog_traps_focus_supports_escape_and_restores_the_opener()
    {
        Assert.Contains("aria-describedby=\"et-analytics-description\"", Analytics, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", Analytics, StringComparison.Ordinal);
        Assert.Contains("let lastFocused=null", Runtime, StringComparison.Ordinal);
        Assert.Contains("const restoreFocus=()=>", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(event.key==='Escape')", Runtime, StringComparison.Ordinal);
        Assert.Contains("if(event.key!=='Tab')return", Runtime, StringComparison.Ordinal);
        Assert.Contains("event.shiftKey&&document.activeElement===first", Runtime, StringComparison.Ordinal);
        Assert.Contains("document.activeElement===last", Runtime, StringComparison.Ordinal);
        Assert.Contains("preferences.addEventListener('click',()=>showConsent(preferences))", Runtime, StringComparison.Ordinal);
        Assert.Contains("hideConsent(true)", Runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_public_analytics_api_remains_available_to_all_event_views()
    {
        Assert.Contains("root.makePayAnalytics=Object.freeze({track,trackOnce,purchaseOnce", Runtime, StringComparison.Ordinal);
        Assert.Contains("openConsent:", Runtime, StringComparison.Ordinal);
        Assert.Contains("isCollecting:", Runtime, StringComparison.Ordinal);

        foreach (var view in new[] { "Storefront", "Event", "Cart", "Details", "Payment", "Order" })
        {
            var source = Source("Views", "EventTickets", "Public", $"{view}.cshtml");
            Assert.Contains("makePayAnalytics", source, StringComparison.Ordinal);
        }
    }

    private static string Section(string start, string end)
    {
        var startIndex = Runtime.IndexOf(start, StringComparison.Ordinal);
        var endIndex = Runtime.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return Runtime[startIndex..endIndex];
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(RepositoryFile(["src", "BTCPayServer.Plugins.MakePay.EventTickets", .. segments]));

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
