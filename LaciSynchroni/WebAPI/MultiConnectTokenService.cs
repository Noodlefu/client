using System;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LaciSynchroni.WebAPI
{
    public class MultiConnectTokenService
    {
        private readonly ConcurrentDictionary<Guid, ServerHubTokenProvider> _tokenProviders;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ServerConfigurationManager _serverConfigurationManager;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly SyncMediator _syncMediator;
        private readonly HttpClient _httpClient;
        
        public MultiConnectTokenService(HttpClient httpClient, SyncMediator syncMediator, DalamudUtilService dalamudUtilService, ILoggerFactory loggerFactory, ServerConfigurationManager serverConfigurationManager)
        {
            _httpClient = httpClient;
            _syncMediator = syncMediator;
            _dalamudUtilService = dalamudUtilService;
            _loggerFactory = loggerFactory;
            _serverConfigurationManager = serverConfigurationManager;
            _tokenProviders = new ConcurrentDictionary<Guid, ServerHubTokenProvider>();
        }

        public Task<string?> GetCachedToken(Guid serverUuid)
        {
            return GetTokenProvider(serverUuid).GetToken();
        }

        public Task<string?> GetOrUpdateToken(Guid serverUuid, CancellationToken ct)
        {
            return GetTokenProvider(serverUuid).GetOrUpdateToken(ct);
        }

        public Task<bool> TryUpdateOAuth2LoginTokenAsync(Guid serverUuid, ServerStorage currentServer, bool forced = false)
        {
            return GetTokenProvider(serverUuid).TryUpdateOAuth2LoginTokenAsync(currentServer, forced);
        }
        
        private ServerHubTokenProvider GetTokenProvider(Guid serverUuid)
        {
            return _tokenProviders.GetOrAdd(serverUuid, BuildNewTokenProvider);
        }

        private ServerHubTokenProvider BuildNewTokenProvider(Guid serverUuid)
        {
            return new ServerHubTokenProvider(
                _loggerFactory.CreateLogger<ServerHubTokenProvider>(),
                serverUuid,
                _serverConfigurationManager,
                _dalamudUtilService,
                _syncMediator,
                _httpClient
            );
        }
    }
}