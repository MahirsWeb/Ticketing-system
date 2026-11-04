using Microsoft.Extensions.Logging;

namespace TicketSystemTech.Tests;

/// <summary>Keeps every line a component logs (message and exception text), so a test can assert what did not get written.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception) + " " + exception);
}
