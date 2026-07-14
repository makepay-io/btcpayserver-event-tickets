#nullable enable
using BTCPayServer.Data;
using BTCPayServer.Configuration;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

/// <summary>
/// Reads explicit BTCPay AppData identities and native domain mappings. App
/// records are created through BTCPay's standard create-app flow, never as a
/// side effect of loading a plugin page.
/// </summary>
public sealed class EventTicketsAppService(
    AppService apps,
    PoliciesSettings policies,
    IOptions<BTCPayServerOptions> options)
{
    private readonly string _rootPath = TicketPublicUrl.NormalizeRootPath(options.Value.RootPath);

    public Task<AppData?> Get(string appId) =>
        apps.GetApp(appId, EventTicketsAppType.AppType, includeStore: true);

    public async Task<IReadOnlyList<AppData>> GetForStore(string storeId) => (await apps.GetApps(EventTicketsAppType.AppType))
        .Where(app => !app.Archived && app.StoreDataId.Equals(storeId, StringComparison.Ordinal))
        .OrderBy(app => app.Created)
        .ToList();

    public string? MappedDomain(AppData app)
    {
        var mappings = NativeMappings();
        foreach (var mapping in policies.DomainToAppMapping.Where(item =>
                     string.Equals(item.AppId, app.Id, StringComparison.Ordinal)
                     && item.AppType == EventTicketsAppType.AppType))
        {
            if (TicketPublicUrl.TryGetEffectiveNativeMappedHost(mapping.Domain, mapping.AppId, mapping.AppType,
                    mappings, out var host))
                return host;
        }
        return null;
    }

    /// <summary>
    /// Selects the AppData record actually named by Policies mapping. This
    /// intentionally inspects every active app for the store instead of
    /// assuming that the oldest duplicate is canonical.
    /// </summary>
    public async Task<(AppData? App, string? Domain)> MappingForStore(string storeId)
    {
        var storeApps = await GetForStore(storeId);
        if (storeApps.Count == 0) return (null, null);
        var byId = storeApps.ToDictionary(app => app.Id, StringComparer.Ordinal);
        var mappings = NativeMappings();
        foreach (var mapping in policies.DomainToAppMapping)
        {
            if (mapping.AppType != EventTicketsAppType.AppType || !byId.TryGetValue(mapping.AppId, out var app))
                continue;
            if (TicketPublicUrl.TryGetEffectiveNativeMappedHost(mapping.Domain, mapping.AppId, mapping.AppType,
                    mappings, out var domain))
                return (app, domain);
        }
        return (storeApps[0], null);
    }

    public async Task<string?> MappingWarningForStore(string storeId)
    {
        var storeApps = await GetForStore(storeId);
        if (storeApps.Count == 0) return null;
        var appIds = storeApps.Select(app => app.Id).ToHashSet(StringComparer.Ordinal);
        var mappings = NativeMappings();
        foreach (var mapping in policies.DomainToAppMapping.Where(item =>
                     item.AppType == EventTicketsAppType.AppType && appIds.Contains(item.AppId)))
        {
            if (!TicketPublicUrl.TryGetNativeMappedHost(mapping.Domain, out var domain))
                return "An Event Tickets domain mapping is inactive. Enter the exact canonical ASCII hostname without whitespace, a trailing dot, a scheme, port, or path; use the punycode form for internationalized domains.";

            var duplicates = policies.DomainToAppMapping.Count(item =>
                string.Equals(item.Domain, mapping.Domain, StringComparison.InvariantCultureIgnoreCase));
            if (!TicketPublicUrl.TryGetEffectiveNativeMappedHost(mapping.Domain, mapping.AppId, mapping.AppType,
                    mappings, out _))
                return $"The mapping for {domain} is inactive because an earlier duplicate hostname row points to another App. BTCPay uses the first exact row; remove or reorder the conflicting rows in Server Settings → Policies.";
            if (duplicates > 1)
                return $"Duplicate mapping rows exist for {domain}. BTCPay uses the first exact row; remove the duplicates so the canonical App cannot change unexpectedly.";
        }
        return null;
    }

    public async Task<string?> GetMappedBaseUrl(string storeId)
    {
        var (_, domain) = await MappingForStore(storeId);
        return domain is null ? null : $"https://{domain}{_rootPath}";
    }

    private IReadOnlyList<TicketPublicUrl.NativeAppMapping> NativeMappings() => policies.DomainToAppMapping
        .Select(item => new TicketPublicUrl.NativeAppMapping(item.Domain, item.AppId, item.AppType))
        .ToList();
}
