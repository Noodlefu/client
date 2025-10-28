using System;
using LaciSynchroni.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.SyncConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, TransientConfigService transientConfigService,
    ServerConfigService serverConfigService) : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger = logger;

    public void Migrate()
    {
        if (transientConfigService.Current.Version == 0)
        {
            _logger.LogInformation("Migrating Transient Config V0 => V1");
            transientConfigService.Current.TransientConfigs.Clear();
            transientConfigService.Current.Version = 1;
            transientConfigService.Save();
        }

        var serverConfig = serverConfigService.Current;

        if (serverConfig.Version == 1)
        {
            _logger.LogInformation("Migrating Server Config V1 => V2");
            var centralServer = serverConfig.ServerStorage.Find(f => f.ServerName.Equals("Lunae Crescere Incipientis (Central Server EU)", StringComparison.Ordinal));
            if (centralServer != null)
            {
                centralServer.ServerName = ApiController.MainServer;
            }
            serverConfig.Version = 2;
            serverConfigService.Save();
        }

        if (serverConfig.Version == 2)
        {
            _logger.LogInformation("Migrating Server Config V2 => V3");

            foreach (var server in serverConfig.ServerStorage)
            {
                if (server.ServerUuid == Guid.Empty)
                {
                    server.ServerUuid = Guid.NewGuid();
                }
            }

            if (serverConfig.SelectedServerUuid == Guid.Empty)
            {
                var selectedIndex = serverConfig.LegacyCurrentServerIndex ?? 0;
                if (selectedIndex >= 0 && selectedIndex < serverConfig.ServerStorage.Count)
                {
                    serverConfig.SelectedServerUuid = serverConfig.ServerStorage[selectedIndex].ServerUuid;
                }
                else if (serverConfig.ServerStorage.Count > 0)
                {
                    serverConfig.SelectedServerUuid = serverConfig.ServerStorage[0].ServerUuid;
                }
            }
            serverConfig.LegacyCurrentServerIndex = null;
            serverConfig.Version = 3;
            serverConfigService.Save();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
