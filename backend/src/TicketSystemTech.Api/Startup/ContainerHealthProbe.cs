namespace TicketSystemTech.Api.Startup;

/// <summary>
/// <c>dotnet TicketSystemTech.Api.dll --healthcheck</c>: asks the application running in the same container whether it is alive
/// (<c>/health/live</c>) and exits 0 or 1. The runtime image has neither curl nor wget, so Docker's HEALTHCHECK uses this. It does
/// not start the web host, only reads the port it listens on and the host names it accepts (<c>AllowedHosts</c> answers 400 to a
/// request that names any other host, and on a server that list is the real name, not <c>localhost</c>).
/// </summary>
public static class ContainerHealthProbe
{
    public static async Task<int> RunAsync(Func<string, string?>? environment = null, TimeSpan? timeout = null, string? baseDirectory = null)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(4) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{ListeningPort(environment)}/health/live");
            request.Headers.Host = HostToSend(environment, baseDirectory);
            using var response = await http.SendAsync(request);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>
    /// A host name the application accepts: the first of <c>AllowedHosts</c> (the environment variable, else appsettings), a wildcard
    /// entry (<c>*.firma.ba</c>) given a made-up subdomain, and <c>localhost</c> when the list is empty or <c>*</c>.
    /// </summary>
    public static string HostToSend(Func<string, string?>? environment = null, string? baseDirectory = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        var allowed = environment("AllowedHosts");
        if (string.IsNullOrWhiteSpace(allowed))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(baseDirectory ?? AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment("ASPNETCORE_ENVIRONMENT") ?? environment("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();
            allowed = configuration["AllowedHosts"];
        }

        var first = allowed?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first switch
        {
            null or "*" => "localhost",
            _ when first.StartsWith("*.", StringComparison.Ordinal) => "health" + first[1..],
            _ => first,
        };
    }

    /// <summary>The first port the application is told to listen on (ASPNETCORE_HTTP_PORTS, else ASPNETCORE_URLS), 8080 like the image.</summary>
    public static int ListeningPort(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        var ports = environment("ASPNETCORE_HTTP_PORTS");
        if (!string.IsNullOrWhiteSpace(ports)
            && int.TryParse(ports.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var port))
            return port;

        var urls = environment("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var first = urls.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            // "http://+:8080" and "http://*:8080" are not valid URIs; the host part does not matter here.
            if (first is not null && Uri.TryCreate(first.Replace("+", "localhost").Replace("*", "localhost"), UriKind.Absolute, out var uri) && uri.Port > 0)
                return uri.Port;
        }

        return 8080;
    }
}
