using System;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LaciSynchroni.WebAPI;

public sealed partial class ApiController : DisposableMediatorSubscriberBase
{
    public const string MainServer = "Laci Synchroni";
    public const string MainServiceUri = "wss://sinus.syrilai.dev";

    private readonly DalamudUtilService _dalamudUtil;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILoggerProvider _loggerProvider;
    private readonly MultiConnectTokenService _multiConnectTokenService;
    private readonly SyncConfigService _syncConfigService;
    private readonly HttpClient _httpClient;

    private readonly ConcurrentDictionary<Guid, SyncHubClient> _syncClients = new();

    public ApiController(ILogger<ApiController> logger, ILoggerFactory loggerFactory, DalamudUtilService dalamudUtil, ILoggerProvider loggerProvider,
        PairManager pairManager, ServerConfigurationManager serverConfigManager, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) : base(logger, mediator)
    {
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _multiConnectTokenService = multiConnectTokenService;
        _syncConfigService = syncConfigService;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _loggerProvider = loggerProvider;

        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DisposeAllClients());
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => AutoConnectClients());
    }

    public ServerState GetServerState(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?._serverState ?? ServerState.Disconnected;
    }

    public bool IsServerConnected(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?._serverState == ServerState.Connected;
    }

    public string GetServerName(Guid serverUuid)
    {
        return _serverConfigManager.GetServerByUuid(serverUuid).ServerName ?? string.Empty;
    }

    public int GetOnlineUsersForServer(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.SystemInfoDto?.OnlineUsers ?? 0;
    }

    public bool IsServerAlive(Guid serverUuid)
    {
        var serverState = GetServerState(serverUuid);
        return serverState is ServerState.Connected or ServerState.Disconnected;
    }

    public int OnlineUsers => _syncClients.Sum(entry => entry.Value.SystemInfoDto?.OnlineUsers ?? 0);

    public ServerInfo? GetServerInfoForServer(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.ConnectionDto?.ServerInfo;
    }

    public DefaultPermissionsDto? GetDefaultPermissionsForServer(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.ConnectionDto?.DefaultPreferredPermissions;
    }

    public bool AnyServerConnected => _syncClients.Any(client => client.Value._serverState == ServerState.Connected);

    public bool AnyServerConnecting => _syncClients.Any(client => client.Value._serverState == ServerState.Connecting);

    public bool AnyServerDisconnecting => _syncClients.Any(client => client.Value._serverState == ServerState.Disconnecting);

    public Guid[] ConnectedServerUuids =>
    [
        .. _syncClients.Where(p => p.Value._serverState == ServerState.Connected).Select(p => p.Key)
    ];

    public bool IsServerConnecting(Guid serverUuid)
    {
        return GetServerState(serverUuid) == ServerState.Connecting;
    }

    public int GetMaxGroupsJoinedByUser(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.ConnectionDto?.ServerInfo.MaxGroupsJoinedByUser ?? 0;
    }

    public int GetMaxGroupsCreatedByUser(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.ConnectionDto?.ServerInfo.MaxGroupsCreatedByUser ?? 0;
    }

    public string? GetAuthFailureMessageByServer(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.AuthFailureMessage;
    }

    public string GetUidByServer(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.UID ?? string.Empty;
    }

    public string GetDisplayNameByServer(Guid serverUuid)
    {
        return GetClientForServer(serverUuid)?.ConnectionDto?.User.AliasOrUID ?? string.Empty;
    }

    public async Task PauseConnectionAsync(Guid serverUuid)
    {
        _syncClients.TryRemove(serverUuid, out SyncHubClient? removed);
        if (removed != null)
        {
            await removed.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task CreateConnectionsAsync(Guid serverUuid)
    {
        await ConnectMultiClient(serverUuid).ConfigureAwait(false);
    }

    public Task CyclePauseAsync(Guid serverUuid, UserData userData)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        _ = Task.Run(async () =>
        {
            await GetOrCreateForServer(serverUuid).CyclePauseAsync(serverUuid, userData).ConfigureAwait(false);
        }, cts.Token);
        return Task.CompletedTask;
    }

    private SyncHubClient CreateNewClient(Guid serverUuid)
    {
        return new SyncHubClient(serverUuid, _serverConfigManager, _pairManager, _dalamudUtil,
            _loggerFactory, _loggerProvider, Mediator, _multiConnectTokenService, _syncConfigService, _httpClient);
    }

    private SyncHubClient? GetClientForServer(Guid serverUuid)
    {
        _syncClients.TryGetValue(serverUuid, out var client);
        return client;
    }

    private SyncHubClient GetOrCreateForServer(Guid serverUuid)
    {
        return _syncClients.GetOrAdd(serverUuid, CreateNewClient);
    }

    private Task ConnectMultiClient(Guid serverUuid)
    {
        return GetOrCreateForServer(serverUuid).CreateConnectionsAsync();
    }

    public void AutoConnectClients()
    {
        _ = Task.Run(async () =>
        {
            foreach (var server in _serverConfigManager.ServerUuids)
            {
                var serverStorage = _serverConfigManager.GetServerByUuid(server);
                if (!serverStorage.FullPause)
                {
                    await GetOrCreateForServer(server).DalamudUtilOnLogIn().ConfigureAwait(false);
                }
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        DisposeAllClients();
        base.Dispose(disposing);
    }

    private void DisposeAllClients()
    {
        foreach (var syncHubClient in _syncClients.Values)
        {
            syncHubClient.Dispose();
        }
        _syncClients.Clear();
    }

    public ConnectionDto? GetConnectionDto(Guid serverUuid)
    {
        if (!IsServerConnected(serverUuid))
        {
            return null;
        }
        return _syncClients[serverUuid].ConnectionDto;
    }
}