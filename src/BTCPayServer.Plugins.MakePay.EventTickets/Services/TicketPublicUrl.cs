#nullable enable
using System.Globalization;
using System.Net;
using BTCPayServer.Plugins.MakePay.EventTickets.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public static class TicketPublicUrl
{
    public readonly record struct NativeAppMapping(string? Domain, string? AppId, string? AppType);

    public const string LegacyController = "EventTicketsPublic";
    public const string CleanController = "CleanEventTicketsPublic";

    public static string NormalizeRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || rootPath == "/") return "";
        return "/" + rootPath.Trim('/');
    }

    public static string ToAbsoluteHttpUrl(string pathOrUrl, string requestScheme, string requestHost)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absoluteUrl)
            && (absoluteUrl.Scheme == Uri.UriSchemeHttp || absoluteUrl.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUrl.ToString();
        }

        var path = pathOrUrl.StartsWith('/') ? pathOrUrl : $"/{pathOrUrl}";
        return $"{requestScheme}://{requestHost}{path}";
    }

    public static bool TryNormalizeHost(string? value, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        var candidate = value?.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "Enter a dedicated hostname, for example tickets.example.com.";
            return false;
        }
        if (candidate.Length > 253 || candidate.Any(char.IsWhiteSpace) || candidate.IndexOfAny(['/', '\\', '?', '#', '@', '*', ':']) >= 0)
        {
            error = "Enter only a hostname without a scheme, port, path, query, or wildcard.";
            return false;
        }

        try
        {
            candidate = new IdnMapping().GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            error = "The hostname contains an invalid internationalized domain label.";
            return false;
        }

        if (candidate is "localhost" || candidate.EndsWith(".localhost", StringComparison.Ordinal) ||
            IPAddress.TryParse(candidate, out _) || candidate.Length > 253)
        {
            error = "Use a public DNS hostname, not localhost or an IP address.";
            return false;
        }

        var labels = candidate.Split('.');
        if (labels.Length < 2 || labels.Any(label => label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-' || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            error = "Enter a valid public DNS hostname such as tickets.example.com.";
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// BTCPay's DomainMappingConstraint compares the stored value directly to
    /// Request.Host.Host. A value that only becomes valid after trimming,
    /// removing a trailing dot, or IDN conversion would therefore produce a
    /// canonical URL which the upstream constraint cannot serve.
    /// </summary>
    public static bool TryGetNativeMappedHost(string? value, out string normalized)
    {
        normalized = "";
        if (!TryNormalizeHost(value, out var candidate, out _)
            || !string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            return false;
        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Mirrors DomainMappingConstraint's first exact-domain-row behavior. A
    /// later duplicate cannot become canonical when an earlier row selects a
    /// different App or App type.
    /// </summary>
    public static bool TryGetEffectiveNativeMappedHost(
        string? domain,
        string appId,
        string appType,
        IEnumerable<NativeAppMapping> mappings,
        out string normalized)
    {
        if (!TryGetNativeMappedHost(domain, out normalized)) return false;
        var first = mappings.FirstOrDefault(item =>
            string.Equals(item.Domain, domain, StringComparison.InvariantCultureIgnoreCase));
        return first.Domain is not null
               && string.Equals(first.AppId, appId, StringComparison.Ordinal)
               && string.Equals(first.AppType, appType, StringComparison.Ordinal);
    }

    public static string PublicPath(bool clean, string storeId, string suffix = "")
    {
        var prefix = clean ? "/events" : $"/stores/{Uri.EscapeDataString(storeId)}/events";
        if (string.IsNullOrEmpty(suffix)) return prefix;
        return prefix + (suffix[0] == '/' ? suffix : "/" + suffix);
    }

    public static string EventPath(bool clean, string storeId, string eventSlug) =>
        PublicPath(clean, storeId, Uri.EscapeDataString(eventSlug));

    public static string OrderPath(bool clean, string storeId, string orderId, string? accessToken = null)
    {
        var path = PublicPath(clean, storeId, $"order/{Uri.EscapeDataString(orderId)}");
        return string.IsNullOrWhiteSpace(accessToken) ? path : path + "?accessToken=" + Uri.EscapeDataString(accessToken);
    }

    public static string PublicBaseUrl(string? mappedBaseUrl, string legacyBaseUrl) =>
        string.IsNullOrWhiteSpace(mappedBaseUrl) ? legacyBaseUrl.TrimEnd('/') : mappedBaseUrl.TrimEnd('/');

    public static bool IsOnionBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

    public static void CaptureOrderOrigin(
        TicketOrder order,
        bool cleanUrls,
        bool isOnion,
        string? mappedBaseUrl,
        string requestBaseUrl)
    {
        var preferCleanUrls = !isOnion && (cleanUrls || !string.IsNullOrWhiteSpace(mappedBaseUrl));
        order.PreferCleanUrls = preferCleanUrls;
        order.PublicBaseUrl = PublicBaseUrl(preferCleanUrls ? mappedBaseUrl : null, requestBaseUrl);
    }

    public static string OrderUrl(TicketOrder order, string? accessToken, string? mappedBaseUrl)
    {
        // Null is the backward-compatible state for orders persisted before
        // origin intent was recorded. Preserve a stored onion origin across
        // upgrades; non-onion legacy orders retain the historical behavior of
        // following a newly configured native mapping.
        var preferCleanUrls = order.PreferCleanUrls
                              ?? (!IsOnionBaseUrl(order.PublicBaseUrl)
                                  && !string.IsNullOrWhiteSpace(mappedBaseUrl));
        if (preferCleanUrls)
        {
            var cleanBase = PublicBaseUrl(mappedBaseUrl, order.PublicBaseUrl);
            return cleanBase + OrderPath(true, order.StoreId, order.Id, accessToken);
        }

        var legacyBase = order.PublicBaseUrl.TrimEnd('/');
        return legacyBase + OrderPath(false, order.StoreId, order.Id, accessToken);
    }

    public static void BindMappedStore(
        string storeId,
        IDictionary<string, object?> actionArguments,
        RouteValueDictionary routeValues,
        ModelStateDictionary modelState)
    {
        routeValues["storeId"] = storeId;
        if (actionArguments.ContainsKey("storeId")) actionArguments["storeId"] = storeId;
        foreach (var key in modelState.Keys
                     .Where(key => string.Equals(key, "storeId", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            modelState.Remove(key);
    }

    public static string? CleanUrlFromLegacy(string? mappedBaseUrl, string storeId, PathString pathBase, PathString path, QueryString query)
    {
        if (string.IsNullOrWhiteSpace(mappedBaseUrl)) return null;
        var legacyPrefix = pathBase.Add(new PathString($"/stores/{storeId}/events"));
        var currentPath = pathBase.Add(path);
        if (!currentPath.StartsWithSegments(legacyPrefix, out var remainder)) return null;
        // mappedBaseUrl already contains BTCPay's configured RootPath. PathBase
        // is used to recognize the incoming legacy route but must not be added
        // a second time to the mapped destination.
        return mappedBaseUrl.TrimEnd('/') + new PathString("/events").Add(remainder) + query;
    }
}
