using Dalamud.Utility;
using LaciSynchroni.Common.Routes;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System;

namespace LaciSynchroni.Services.ServerConfiguration;

public class ServerConfigurationManager
{
    private readonly ServerConfigService _serverConfigService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly SyncConfigService _syncConfigService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerConfigurationManager> _logger;
    private readonly SyncMediator _syncMediator;
    private readonly NotesConfigService _notesConfig;
    private readonly ServerTagConfigService _serverTagConfig;

    public ServerConfigurationManager(ILogger<ServerConfigurationManager> logger, ServerConfigService serverConfigService,
        ServerTagConfigService serverTagConfig, NotesConfigService notesConfig, DalamudUtilService dalamudUtil,
        SyncConfigService syncConfigService, HttpClient httpClient, SyncMediator syncMediator)
    {
        _logger = logger;
        _serverConfigService = serverConfigService;
        _serverTagConfig = serverTagConfig;
        _notesConfig = notesConfig;
        _dalamudUtil = dalamudUtil;
        _syncConfigService = syncConfigService;
        _httpClient = httpClient;
        _syncMediator = syncMediator;

        EnsureServerUuids();
        EnsureServerTagConsistency();
        EnsureServerNotesConsistency();
    }

    public IEnumerable<Guid> ServerUuids => _serverConfigService.Current.ServerStorage.Select(server => server.ServerUuid);

    public bool AnyServerConfigured => _serverConfigService.Current.ServerStorage.Count > 0;
    public bool SendCensusData
    {
        get
        {
            return _serverConfigService.Current.SendCensusData;
        }
        set
        {
            _serverConfigService.Current.SendCensusData = value;
            _serverConfigService.Save();
        }
    }

    public bool ShownCensusPopup
    {
        get
        {
            return _serverConfigService.Current.ShownCensusPopup;
        }
        set
        {
            _serverConfigService.Current.ShownCensusPopup = value;
            _serverConfigService.Save();
        }
    }

    public (string OAuthToken, string UID)? GetOAuth2(Guid serverUuid, out bool hasMulti)
    {
        ServerStorage currentServer = GetServerByUuid(serverUuid);
        hasMulti = false;

        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        var cid = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult();

        var auth = currentServer.Authentications.FindAll(f => string.Equals(f.CharacterName, charaName) && f.WorldId == worldId);
        if (auth.Count >= 2)
        {
            _logger.LogTrace("GetOAuth2 accessed, returning null because multiple ({count}) identical characters.", auth.Count);
            hasMulti = true;
            return null;
        }

        if (auth.Count == 0)
        {
            _logger.LogTrace("GetOAuth2 accessed, returning null because no set up characters for {chara} on {world}", charaName, worldId);
            return null;
        }

        if (auth.Single().LastSeenCID != cid)
        {
            auth.Single().LastSeenCID = cid;
            _logger.LogTrace("GetOAuth2 accessed, updating CID for {chara} on {world} to {cid}", charaName, worldId, cid);
            Save();
        }

        if (!string.IsNullOrEmpty(auth.Single().UID) && !string.IsNullOrEmpty(currentServer.OAuthToken))
        {
            _logger.LogTrace("GetOAuth2 accessed, returning {key} ({keyValue}) for {chara} on {world}", auth.Single().UID, string.Join("", currentServer.OAuthToken.Take(10)), charaName, worldId);
            return (currentServer.OAuthToken, auth.Single().UID!);
        }

        _logger.LogTrace("GetOAuth2 accessed, returning null because no UID found for {chara} on {world} or OAuthToken is not configured.", charaName, worldId);

        return null;
    }

    public string? GetSecretKey(Guid serverUuid, out bool hasMulti)
    {
        var currentServer = GetServerByUuid(serverUuid);
        hasMulti = false;

        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        var cid = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult();
        if (!currentServer.Authentications.Any() && currentServer.SecretKeys.Any())
        {
            currentServer.Authentications.Add(new Authentication()
            {
                CharacterName = charaName,
                WorldId = worldId,
                LastSeenCID = cid,
                SecretKeyIdx = currentServer.SecretKeys.Last().Key,
            });

            Save();
        }

        var auth = currentServer.Authentications.FindAll(f => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
        if (auth.Count >= 2)
        {
            _logger.LogTrace("GetSecretKey accessed, returning null because multiple ({count}) identical characters.", auth.Count);
            hasMulti = true;
            return null;
        }

        if (auth.Count == 0)
        {
            _logger.LogTrace("GetSecretKey accessed, returning null because no set up characters for {chara} on {world}", charaName, worldId);
            return null;
        }

        if (auth.Single().LastSeenCID != cid)
        {
            auth.Single().LastSeenCID = cid;
            _logger.LogTrace("GetSecretKey accessed, updating CID for {chara} on {world} to {cid}", charaName, worldId, cid);
            Save();
        }

        if (currentServer.SecretKeys.TryGetValue(auth.Single().SecretKeyIdx, out var secretKey))
        {
            _logger.LogTrace("GetSecretKey accessed, returning {key} ({keyValue}) for {chara} on {world}", secretKey.FriendlyName, string.Join("", secretKey.Key.Take(10)), charaName, worldId);
            return secretKey.Key;
        }

        _logger.LogTrace("GetSecretKey accessed, returning null because no fitting key found for {chara} on {world} for idx {idx}.", charaName, worldId, auth.Single().SecretKeyIdx);

        return null;
    }

    public string[] GetServerApiUrls()
    {
        return _serverConfigService.Current.ServerStorage.Select(v => v.ServerUri).ToArray();
    }

    public string GetServerName(Guid serverUuid)
    {
        return GetServerByUuid(serverUuid).ServerName;
    }

    public ServerStorage GetServerByUuid(Guid serverUuid)
    {
        return _serverConfigService.Current.ServerStorage.First(server => server.ServerUuid == serverUuid);
    }

    public string GetDiscordUserFromToken(ServerStorage server)
    {
        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        if (string.IsNullOrEmpty(server.OAuthToken)) return string.Empty;
        try
        {
            var token = handler.ReadJwtToken(server.OAuthToken);
            return token.Claims.First(f => string.Equals(f.Type, "discord_user", StringComparison.Ordinal)).Value!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read jwt, resetting it");
            server.OAuthToken = null;
            Save();
            return string.Empty;
        }
    }

    public List<ServerInfoDto> GetServerInfo()
    {
        var items = _serverConfigService.Current.ServerStorage
            .Select(v => new ServerInfoDto
            {
                Id = v.ServerUuid,
                Name = v.ServerName,
                Uri = v.ServerUri,
                HubUri = v.ServerHubUri
            }).ToList();
        return items;
    }

    public string[] GetServerNames()
    {
        return _serverConfigService.Current.ServerStorage.Select(v => v.ServerName).ToArray();
    }

    public bool HasValidConfig()
    {
        return _serverConfigService.Current.ServerStorage.Count > 0 &&
               _serverConfigService.Current.ServerStorage.Exists(server => server.Authentications.Count >= 1);
    }

    public void Save()
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        _logger.LogDebug("{caller} Calling config save", caller);
        _serverConfigService.Save();
    }

    internal void AddCurrentCharacterToServer(Guid serverUuid)
    {
        var server = GetServerByUuid(serverUuid);
        if (server.Authentications.Any(c => string.Equals(c.CharacterName, _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(), StringComparison.Ordinal)
                && c.WorldId == _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult()))
            return;

        server.Authentications.Add(new Authentication()
        {
            CharacterName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(),
            WorldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult(),
            SecretKeyIdx = !server.UseOAuth2 ? server.SecretKeys.Last().Key : -1,
            LastSeenCID = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult(),
        });
        Save();
    }

    internal void AddEmptyCharacterToServer(Guid serverUuid)
    {
        var server = GetServerByUuid(serverUuid);
        server.Authentications.Add(new Authentication()
        {
            SecretKeyIdx = server.SecretKeys.Any() ? server.SecretKeys.First().Key : -1,
        });
        Save();
    }

    internal void AddOpenPairTag(Guid serverUuid, string tag)
    {
        GetTagStorageForUuid(serverUuid).OpenPairTags.Add(tag);
        _serverTagConfig.Save();
    }
    
    internal void AddGlobalOpenPairTag(string tag)
    {
        _serverTagConfig.Current.GlobalTagStorage.OpenPairTags.Add(tag);
        _serverTagConfig.Save();
    }
    
    internal void RemoveOpenGlobalPairTag(string tag)
    {
        _serverTagConfig.Current.GlobalTagStorage.OpenPairTags.Remove(tag);
        _serverTagConfig.Save();
    }

    internal void AddServer(ServerStorage serverStorage)
    {
        _serverConfigService.Current.ServerStorage.Add(serverStorage);
        Save();
    }
    
    internal void SetFirstServer(ServerStorage serverStorage)
    {
        _serverConfigService.Current.ServerStorage.Clear();
        _serverConfigService.Current.ServerStorage.Add(serverStorage);
        Save();
    }

    internal void AddTag(Guid serverUuid, string tag)
    {
        GetTagStorageForUuid(serverUuid).ServerAvailablePairTags.Add(tag);
        _serverTagConfig.Save();
        _syncMediator.Publish(new RefreshUiMessage());
    }

    internal void AddTagForUid(Guid serverUuid, string uid, string tagName)
    {
        if (GetTagStorageForUuid(serverUuid).UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Add(tagName);
            _syncMediator.Publish(new RefreshUiMessage());
        }
        else
        {
            GetTagStorageForUuid(serverUuid).UidServerPairedUserTags[uid] = [tagName];
        }

        _serverTagConfig.Save();
    }

    internal bool ContainsOpenPairTag(Guid serverUuid, string tag)
    {
        return GetTagStorageForUuid(serverUuid).OpenPairTags.Contains(tag);
    }
    
    internal bool ContainsGlobalOpenPairTag(string tag)
    {
        return _serverTagConfig.Current.GlobalTagStorage.OpenPairTags.Contains(tag);
    }

    internal bool ContainsTag(Guid serverUuid, string uid, string tag)
    {
        if (GetTagStorageForUuid(serverUuid).UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Contains(tag, StringComparer.Ordinal);
        }

        return false;
    }

    internal void DeleteServer(ServerStorage selectedServer)
    {
        _serverConfigService.Current.ServerStorage.Remove(selectedServer);
        Save();
    }

    internal string? GetNoteForGid(Guid serverUuid, string gID)
    {
        if (NotesStorageForUuid(serverUuid).GidServerComments.TryGetValue(gID, out var note))
        {
            if (string.IsNullOrEmpty(note)) return null;
            return note;
        }

        return null;
    }

    internal string? GetNoteForUid(Guid serverUuid, string uid)
    {
        if (NotesStorageForUuid(serverUuid).UidServerComments.TryGetValue(uid, out var note))
        {
            if (string.IsNullOrEmpty(note)) return null;
            return note;
        }
        return null;
    }

    internal HashSet<string> GetServerAvailablePairTags(Guid serverUuid)
    {
        return GetTagStorageForUuid(serverUuid).ServerAvailablePairTags;
    }

    internal Dictionary<string, List<string>> GetUidServerPairedUserTags(Guid serverUuid)
    {
        return GetTagStorageForUuid(serverUuid).UidServerPairedUserTags;
    }

    internal HashSet<string> GetUidsForTag(Guid serverUuid, string tag)
    {
        return GetTagStorageForUuid(serverUuid).UidServerPairedUserTags.Where(p => p.Value.Contains(tag, StringComparer.Ordinal)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
    }

    internal bool HasTags(Guid serverUuid, string uid)
    {
        if (GetTagStorageForUuid(serverUuid).UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Any();
        }

        return false;
    }

    internal void RemoveCharacterFromServer(Guid serverUuid, Authentication item)
    {
        var server = GetServerByUuid(serverUuid);
        server.Authentications.Remove(item);
        Save();
    }

    internal void RemoveOpenPairTag(Guid serverUuid, string tag)
    {
        GetTagStorageForUuid(serverUuid).OpenPairTags.Remove(tag);
        _serverTagConfig.Save();
    }

    internal void RemoveTag(Guid serverUuid, string tag)
    {
        GetTagStorageForUuid(serverUuid).ServerAvailablePairTags.Remove(tag);
        foreach (var uid in GetUidsForTag(serverUuid, tag))
        {
            RemoveTagForUid(serverUuid, uid, tag, save: false);
        }
        _serverTagConfig.Save();
        _syncMediator.Publish(new RefreshUiMessage());
    }

    internal void RemoveTagForUid(Guid serverUuid, string uid, string tagName, bool save = true)
    {
        if (GetTagStorageForUuid(serverUuid).UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Remove(tagName);

            if (save)
            {
                _serverTagConfig.Save();
                _syncMediator.Publish(new RefreshUiMessage());
            }
        }
    }

    internal void SaveNotes()
    {
        _notesConfig.Save();
    }

    internal void SetNoteForGid(Guid serverUuid, string gid, string note, bool save = true)
    {
        if (string.IsNullOrEmpty(gid)) return;

        NotesStorageForUuid(serverUuid).GidServerComments[gid] = note;
        if (save)
            SaveNotes();
    }

    internal void SetNoteForUid(Guid serverUuid, string uid, string note, bool save = true)
    {
        if (string.IsNullOrEmpty(uid)) return;

        NotesStorageForUuid(serverUuid).UidServerComments[uid] = note;
        if (save)
            SaveNotes();
    }

    internal void AutoPopulateNoteForUid(Guid serverUuid, string uid, string note)
    {
        if (!_syncConfigService.Current.AutoPopulateEmptyNotesFromCharaName
            || GetNoteForUid(serverUuid, uid) != null)
            return;

        SetNoteForUid(serverUuid, uid, note, save: true);
    }

    private ServerNotesStorage NotesStorageForUuid(Guid serverUuid)
    {
        var serverUri = GetServerByUuid(serverUuid).ServerUri;
        TryCreateNotesStorage(serverUri);
        return _notesConfig.Current.ServerNotes[serverUri];
    }

    private ServerTagStorage GetTagStorageForUuid(Guid serverUuid)
    {
        var serverUri = GetServerByUuid(serverUuid).ServerUri;
        TryCreateCurrentServerTagStorage(serverUri);
        return _serverTagConfig.Current.ServerTagStorage[serverUri];
    }

    private void TryCreateNotesStorage(string apiUrl)
    {
        if (!_notesConfig.Current.ServerNotes.ContainsKey(apiUrl))
        {
            _notesConfig.Current.ServerNotes[apiUrl] = new();
        }
    }

    private void TryCreateCurrentServerTagStorage(string apiUrl)
    {
        if (!_serverTagConfig.Current.ServerTagStorage.ContainsKey(apiUrl))
        {
            _serverTagConfig.Current.ServerTagStorage[apiUrl] = new();
        }
    }

    private void EnsureServerUuids()
    {
        var serverConfig = _serverConfigService.Current;
        bool updated = false;

        foreach (var server in serverConfig.ServerStorage)
        {
            if (server.ServerUuid == Guid.Empty)
            {
                server.ServerUuid = Guid.NewGuid();
                updated = true;
            }
        }

        if (serverConfig.SelectedServerUuid == Guid.Empty && serverConfig.ServerStorage.Count > 0)
        {
            serverConfig.SelectedServerUuid = serverConfig.ServerStorage[0].ServerUuid;
            updated = true;
        }

        if (updated)
        {
            _serverConfigService.Save();
        }
    }

    private void EnsureServerTagConsistency()
    {
        foreach (var server in _serverConfigService.Current.ServerStorage)
        {
            TryCreateCurrentServerTagStorage(server.ServerUri);
        }
    }

    private void EnsureServerNotesConsistency()
    {
        foreach (var server in _serverConfigService.Current.ServerStorage)
        {
            TryCreateNotesStorage(server.ServerUri);
        }
    }

    public async Task<Dictionary<string, string>> GetUIDsWithDiscordToken(string serverUri, string token)
    {
        try
        {
            var baseUri = serverUri.Replace("wss://", "https://").Replace("ws://", "http://");
            var oauthCheckUri = AuthRoutes.GetUIDsFullPath(new Uri(baseUri));
            using var request = new HttpRequestMessage(HttpMethod.Get, oauthCheckUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(responseStream).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure getting UIDs");
            return [];
        }
    }

    public async Task<Uri?> CheckDiscordOAuth(string serverUri)
    {
        try
        {
            var baseUri = serverUri.Replace("wss://", "https://").Replace("ws://", "http://");
            var oauthCheckUri = AuthRoutes.GetDiscordOAuthEndpointFullPath(new Uri(baseUri));
            var response = await _httpClient.GetFromJsonAsync<Uri?>(oauthCheckUri).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure checking for Discord Auth");
            return null;
        }
    }

    public async Task<string?> GetDiscordOAuthToken(Uri discordAuthUri, string serverUri, CancellationToken token)
    {
        var sessionId = BitConverter.ToString(RandomNumberGenerator.GetBytes(64)).Replace("-", "").ToLower();
        Util.OpenLink(discordAuthUri.ToString() + "?sessionId=" + sessionId);

        string? discordToken = null;
        using CancellationTokenSource timeOutCts = new();
        timeOutCts.CancelAfter(TimeSpan.FromSeconds(60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeOutCts.Token, token);
        try
        {
            var baseUri = serverUri.Replace("wss://", "https://").Replace("ws://", "http://");
            var oauthCheckUri = AuthRoutes.GetDiscordOAuthTokenFullPath(new Uri(baseUri), sessionId);
            var response = await _httpClient.GetAsync(oauthCheckUri, linkedCts.Token).ConfigureAwait(false);
            discordToken = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure getting Discord Token");
            return null;
        }

        if (discordToken == null)
            return null;

        return discordToken;
    }

    public HttpTransportType GetTransport(Guid serverUuid)
    {
        return GetServerByUuid(serverUuid).HttpTransportType;
    }

    public void SetTransportType(Guid serverUuid, HttpTransportType httpTransportType)
    {
        GetServerByUuid(serverUuid).HttpTransportType = httpTransportType;
        Save();
    }
}