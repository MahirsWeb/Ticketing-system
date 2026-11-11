using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Startup;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The README suggests <c>Startup:Migrate=false</c> for a server. In that mode the application used to skip the check of the keys, and with a
/// wrong <c>Pii:HashKey</c> it started "healthy" while every sign-in failed. The keys are now checked on every start. The start-up tasks are run
/// here directly against the real (throwaway) database, once with the key the data was made with and once with another one.
/// </summary>
[Collection(ApiCollection.Name)]
public class StartupKeyCheckTests
{
    private readonly ApiWorld _world;

    public StartupKeyCheckTests(ApiWorld world) => _world = world;

    /// <summary>The application's own services, except that the protector is the one given (a protector with a different hash key stands
    /// for a server configured with a different <c>Pii__HashKey</c>).</summary>
    private sealed class ServicesWith : IServiceProvider, IServiceScopeFactory
    {
        private readonly IServiceProvider _real;
        private readonly IPiiProtector _protector;

        public ServicesWith(IServiceProvider real, IPiiProtector protector)
        {
            _real = real;
            _protector = protector;
        }

        public object? GetService(Type serviceType) => serviceType == typeof(IServiceScopeFactory) ? this : _real.GetService(serviceType);

        public IServiceScope CreateScope() => new Scope(_real.CreateScope(), new Provider(this));

        private sealed class Provider : IServiceProvider
        {
            private readonly ServicesWith _owner;
            public Provider(ServicesWith owner) => _owner = owner;
            public object? GetService(Type serviceType) => serviceType == typeof(IPiiProtector) ? _owner._protector : _owner.GetService(serviceType);
        }

        private sealed class Scope : IServiceScope
        {
            private readonly IServiceScope _inner;
            public Scope(IServiceScope inner, IServiceProvider provider)
            {
                _inner = inner;
                ServiceProvider = new Override(inner.ServiceProvider, provider);
            }
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() => _inner.Dispose();
        }

        /// <summary>Everything from the real scope (the database context), except the protector.</summary>
        private sealed class Override : IServiceProvider
        {
            private readonly IServiceProvider _scoped;
            private readonly IServiceProvider _overrides;
            public Override(IServiceProvider scoped, IServiceProvider overrides)
            {
                _scoped = scoped;
                _overrides = overrides;
            }
            public object? GetService(Type serviceType) => serviceType == typeof(IPiiProtector) ? _overrides.GetService(serviceType) : _scoped.GetService(serviceType);
        }
    }

    private Exception? Run(string hashKey)
    {
        var real = _world.Factory.Services;
        var protector = new PiiProtector(real.GetRequiredService<IDataProtectionProvider>(), Options.Create(new PiiOptions { HashKey = hashKey }),
            real.GetRequiredService<IHostEnvironment>());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Startup:Migrate"] = "false" })
            .Build();
        try
        {
            StartupTasks.RunAsync(new ServicesWith(real, protector), configuration, NullLogger.Instance, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [SqlFact]
    public async Task With_migrations_switched_off_a_wrong_hash_key_still_stops_the_start()
    {
        var failure = Run(new string('x', 48)); // not the key this database was made with

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("Pii:HashKey", failure!.Message);
        await Task.CompletedTask;
    }

    [SqlFact]
    public async Task With_migrations_switched_off_the_right_key_starts_normally()
    {
        var failure = Run(new string('h', 48)); // the key ApiWorld made the database with

        Assert.Null(failure);
        await Task.CompletedTask;
    }
}
