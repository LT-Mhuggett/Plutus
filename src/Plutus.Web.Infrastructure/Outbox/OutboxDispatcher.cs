using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.Outbox
{
    /// <summary>
    /// Broker-less outbox relay (T1.5): a hosted service that, every poll interval, drains the
    /// OutboxEvents table into each registered IEventConsumer via <see cref="OutboxDrainer"/>.
    /// Each consumer keeps its own offset, so they progress independently and a parked (poison)
    /// event on one never halts the others. Inert when the host is on the SQLite dev context.
    /// </summary>
    public sealed class OutboxDispatcher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly OutboxDispatcherOptions _opts;
        private readonly IOutboxEventCodec _codec;
        private readonly ILogger<OutboxDispatcher> _logger;

        public OutboxDispatcher(
            IServiceScopeFactory scopes,
            OutboxDispatcherOptions opts,
            IOutboxEventCodec codec,
            ILogger<OutboxDispatcher> logger)
        {
            _scopes = scopes;
            _opts = opts;
            _codec = codec;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var drainer = new OutboxDrainer(_codec, _opts, _logger);
            var warnedNoMySql = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var ctx = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                    if (ctx == null)
                    {
                        if (!warnedNoMySql)
                        {
                            _logger.LogInformation("OutboxDispatcher idle: no MySqlDbContext (dev/SQLite host).");
                            warnedNoMySql = true;
                        }
                    }
                    else
                    {
                        var consumers = scope.ServiceProvider.GetServices<IEventConsumer>().ToList();
                        foreach (var consumer in consumers)
                            await drainer.DrainConsumerAsync(ctx, consumer, stoppingToken);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OutboxDispatcher iteration failed; retrying next tick.");
                }

                try { await Task.Delay(_opts.PollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public static class OutboxRegistration
    {
        /// <summary>Registers the outbox codec, options and dispatcher hosted service. Consumers
        /// are registered separately (each module adds its own IEventConsumer).</summary>
        public static IServiceCollection AddPlutusOutbox(
            this IServiceCollection services, Action<OutboxDispatcherOptions> configure = null)
        {
            var opts = new OutboxDispatcherOptions();
            configure?.Invoke(opts);
            services.AddSingleton(opts);
            services.AddSingleton<IOutboxEventCodec, DefaultOutboxEventCodec>();
            services.AddHostedService<OutboxDispatcher>();
            return services;
        }
    }
}
