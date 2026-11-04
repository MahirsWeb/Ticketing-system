using Serilog;

namespace TicketSystemTech.Api.Configuration;

/// <summary>
/// Where the log goes: the console (what a container or a developer sees) and a rolling file (what survives the console, and
/// what an IIS or Windows-service installation has instead of one). The file is on by default; <c>Logging:File</c> controls it.
///
/// A password the application generated is NEVER written through here (see DbSeeder), so it cannot end up in a log file.
/// </summary>
public static class LoggingSetup
{
    public const string Template = "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";
    public const string FileTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>The default file name; <c>-</c> and the date are added by the daily rolling (ticketsystem-20261104.log).</summary>
    public const string DefaultFilePath = "logs/ticketsystem-.log";

    public static LoggerConfiguration Configure(LoggerConfiguration logger, IConfiguration configuration, string contentRootPath)
    {
        logger = logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            // Added after FromLogContext so a request's own reference wins; lines written outside a request show "-".
            .Enrich.WithProperty(TicketSystemTech.Api.Middleware.CorrelationIdMiddleware.LogProperty, "-")
            .WriteTo.Console(outputTemplate: Template);

        if (configuration.GetValue("Logging:File:Enabled", true))
        {
            var path = Path.GetFullPath(configuration["Logging:File:Path"] is { Length: > 0 } configured ? configured : DefaultFilePath, contentRootPath);
            logger = logger.WriteTo.File(
                path,
                outputTemplate: FileTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, configuration.GetValue("Logging:File:RetainedFiles", 30)),
                fileSizeLimitBytes: Math.Max(1, configuration.GetValue("Logging:File:MaxFileSizeMb", 100)) * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                shared: true); // the --migrate-only step and the running application may write the same file
        }

        return logger;
    }
}
