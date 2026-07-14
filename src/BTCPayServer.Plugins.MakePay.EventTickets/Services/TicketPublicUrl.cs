namespace BTCPayServer.Plugins.MakePay.EventTickets.Services;

public static class TicketPublicUrl
{
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
}
