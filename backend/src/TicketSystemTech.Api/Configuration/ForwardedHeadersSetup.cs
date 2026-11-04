using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace TicketSystemTech.Api.Configuration;

/// <summary>
/// Which proxies may say who the real visitor is. <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are believed ONLY when the
/// request comes from a proxy that is listed in <c>ForwardedHeaders:KnownProxies</c> (addresses) or
/// <c>ForwardedHeaders:KnownNetworks</c> (e.g. "10.0.0.0/8"), and only one hop is read.
///
/// The framework would also trust the machine itself by default. That default is removed: a request that arrives from
/// <c>127.0.0.1</c> (a script on the server, a tunnel, another service on the same machine) could otherwise write any address
/// it likes into <c>X-Forwarded-For</c> and choose which rate-limit bucket it falls into. When the application really does sit
/// behind a proxy on the same machine, that proxy is listed explicitly (<c>127.0.0.1</c> or <c>::1</c>).
///
/// Careful: with BOTH lists empty the framework's middleware believes EVERY sender, the opposite of what an empty list suggests.
/// So when nothing is listed, forwarding is switched off altogether instead of being left to an empty list.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static ForwardedHeadersOptions Create(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
            options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));

        // Nobody listed: the middleware would trust everybody. Trust nobody instead, by not reading the headers at all.
        if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            options.ForwardedHeaders = ForwardedHeaders.None;

        return options;
    }
}
