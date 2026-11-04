using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TicketSystemTech.Tests;

/// <summary>
/// A small SMTP server on the loopback interface, so the real MailKit client is exercised end to end: greeting, EHLO,
/// optional AUTH PLAIN, MAIL FROM, RCPT TO, DATA, QUIT. Each reply can be changed to make the server busy, refuse a
/// recipient, reject a login, hang up or fall silent. It speaks no TLS (tests use <c>Smtp:Security = None</c>).
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    public sealed record Received(string From, List<string> To, string Data);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<Received> _mails = new();
    private int _connections;

    public int Port { get; }

    /// <summary>When set the server offers AUTH PLAIN and accepts exactly this user and password.</summary>
    public (string User, string Password)? RequiredLogin { get; set; }

    public string GreetingReply { get; set; } = "220 fake ESMTP ready";
    public string RcptReply { get; set; } = "250 OK";
    public string DataReply { get; set; } = "250 queued";

    /// <summary>Close the connection right after the greeting.</summary>
    public bool HangUpAfterGreeting { get; set; }

    /// <summary>Accept the connection and then say nothing at all.</summary>
    public bool StaySilent { get; set; }

    public int Connections => Volatile.Read(ref _connections);

    public IReadOnlyList<Received> Mails
    {
        get { lock (_gate) return _mails.ToList(); }
    }

    public FakeSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    /// <summary>A loopback port nothing listens on (for "connection refused").</summary>
    public static int UnusedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
            // stopped
        }
        catch (ObjectDisposedException)
        {
            // stopped
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                async Task Say(string line) => await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), _stop.Token);

                if (StaySilent)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _stop.Token);
                    return;
                }

                await Say(GreetingReply);
                if (HangUpAfterGreeting) return;

                string from = "";
                var to = new List<string>();
                var authenticated = RequiredLogin is null;

                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    var upper = line.ToUpperInvariant();
                    if (upper.StartsWith("EHLO") || upper.StartsWith("HELO"))
                    {
                        if (RequiredLogin is not null) await Say("250-fake\r\n250-AUTH PLAIN\r\n250 8BITMIME");
                        else await Say("250-fake\r\n250 8BITMIME");
                    }
                    else if (upper.StartsWith("AUTH PLAIN"))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        string payload;
                        if (parts.Length >= 3) payload = parts[2];
                        else
                        {
                            await Say("334 ");
                            payload = await reader.ReadLineAsync(_stop.Token) ?? "";
                        }

                        var fields = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
                        var ok = RequiredLogin is { } login && fields.Length == 3 && fields[1] == login.User && fields[2] == login.Password;
                        authenticated = ok;
                        await Say(ok ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication credentials invalid");
                    }
                    else if (upper.StartsWith("MAIL FROM"))
                    {
                        if (!authenticated) { await Say("530 5.7.0 Authentication required"); continue; }
                        from = Between(line);
                        to.Clear();
                        await Say("250 OK");
                    }
                    else if (upper.StartsWith("RCPT TO"))
                    {
                        if (RcptReply.StartsWith("250")) to.Add(Between(line));
                        await Say(RcptReply);
                    }
                    else if (upper == "DATA")
                    {
                        await Say("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync(_stop.Token) is { } dataLine && dataLine != ".")
                            data.AppendLine(dataLine.StartsWith("..") ? dataLine[1..] : dataLine);

                        if (DataReply.StartsWith("250"))
                        {
                            lock (_gate) _mails.Add(new Received(from, to.ToList(), data.ToString()));
                        }
                        await Say(DataReply);
                    }
                    else if (upper == "RSET" || upper == "NOOP") await Say("250 OK");
                    else if (upper == "QUIT") { await Say("221 bye"); return; }
                    else await Say("502 command not implemented");
                }
            }
            catch (Exception)
            {
                // a client that goes away mid-conversation is part of what is being tested
            }
        }
    }

    private static string Between(string line)
    {
        var start = line.IndexOf('<');
        var end = line.IndexOf('>');
        return start >= 0 && end > start ? line[(start + 1)..end] : line;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
