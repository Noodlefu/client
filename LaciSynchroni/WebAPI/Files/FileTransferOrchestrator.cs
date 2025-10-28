using System;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

namespace LaciSynchroni.WebAPI.Files;

public class FileTransferOrchestrator : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private readonly ConcurrentDictionary<Guid, Uri> _cdnUris = new();
    private readonly HttpClient _httpClient;
    private readonly SyncConfigService _syncConfig;
    private readonly object _semaphoreModificationLock = new();
    private readonly MultiConnectTokenService _multiConnectTokenService;
    private int _availableDownloadSlots;
    private SemaphoreSlim _downloadSemaphore;
    private int CurrentlyUsedDownloadSlots => _availableDownloadSlots - _downloadSemaphore.CurrentCount;

    public FileTransferOrchestrator(ILogger<FileTransferOrchestrator> logger, SyncConfigService syncConfig,
        SyncMediator mediator, HttpClient httpClient, MultiConnectTokenService multiConnectTokenService) : base(logger, mediator)
    {
        _syncConfig = syncConfig;
        _httpClient = httpClient;
        _multiConnectTokenService = multiConnectTokenService;
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        var versionString = string.Create(CultureInfo.InvariantCulture, $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaciSynchroni", versionString));

        _availableDownloadSlots = syncConfig.Current.ParallelDownloads;
        _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);

        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            var newUri = msg.Connection.ServerInfo.FileServerAddress;
            _cdnUris.AddOrUpdate(msg.ServerUuid, _ => newUri, (_, _) => newUri);
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            _cdnUris.TryRemove(msg.ServerUuid, out _);
        });
        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            _downloadReady[msg.RequestId] = true;
        });
    }

    public List<FileTransfer> ForbiddenTransfers { get; } = [];

    public Uri? GetFileCdnUri(Guid serverUuid)
    {
        _cdnUris.TryGetValue(serverUuid, out var uri);
        return uri;
    }

    public void ClearDownloadRequest(Guid guid)
    {
        _downloadReady.Remove(guid, out _);
    }

    public bool IsDownloadReady(Guid guid)
    {
        if (_downloadReady.TryGetValue(guid, out bool isReady) && isReady)
        {
            return true;
        }

        return false;
    }

    public void ReleaseDownloadSlot()
    {
        try
        {
            _downloadSemaphore.Release();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        catch (SemaphoreFullException)
        {
            // ignore
        }
    }

    public async Task<HttpResponseMessage> SendRequestAsync(Guid serverUuid, HttpMethod method, Uri uri,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(serverUuid, requestMessage, ct, httpCompletionOption).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestAsync<T>(Guid serverUuid, HttpMethod method, Uri uri, T content, CancellationToken ct) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        if (content is not ByteArrayContent)
            requestMessage.Content = JsonContent.Create(content);
        else
            requestMessage.Content = content as ByteArrayContent;
        return await SendRequestInternalAsync(serverUuid, requestMessage, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestStreamAsync(Guid serverUuid, HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(serverUuid, requestMessage, ct).ConfigureAwait(false);
    }

    public async Task WaitForDownloadSlotAsync(CancellationToken token)
    {
        lock (_semaphoreModificationLock)
        {
            if (_availableDownloadSlots != _syncConfig.Current.ParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
            {
                _availableDownloadSlots = _syncConfig.Current.ParallelDownloads;
                _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);
            }
        }

        await _downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
        Mediator.Publish(new DownloadLimitChangedMessage());
    }

    public long DownloadLimitPerSlot()
    {
        var limit = _syncConfig.Current.DownloadSpeedLimitInBytes;
        if (limit <= 0) return 0;
        limit = _syncConfig.Current.DownloadSpeedType switch
        {
            DownloadSpeeds.Bps => limit,
            DownloadSpeeds.KBps => limit * 1024,
            DownloadSpeeds.MBps => limit * 1024 * 1024,
            _ => limit,
        };
        var currentUsedDlSlots = CurrentlyUsedDownloadSlots;
        var dividedLimit = limit / (currentUsedDlSlots == 0 ? 1 : currentUsedDlSlots);
        if (dividedLimit < 0)
        {
            Logger.LogWarning("Calculated Bandwidth Limit is negative, returning Infinity: {Value}, CurrentlyUsedDownloadSlots is {CurrentSlots}, DownloadSpeedLimit is {Limit}", dividedLimit, currentUsedDlSlots, limit);
            return long.MaxValue;
        }
        return Math.Clamp(dividedLimit, 1, long.MaxValue);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(Guid serverUuid, HttpRequestMessage requestMessage,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        var token = await _multiConnectTokenService.GetCachedToken(serverUuid).ConfigureAwait(false);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (requestMessage.Content != null && requestMessage.Content is not StreamContent && requestMessage.Content is not ByteArrayContent)
        {
            var content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug("Sending {Method} to {Uri} (Content: {Content})", requestMessage.Method, requestMessage.RequestUri, content);
        }
        else
        {
            Logger.LogDebug("Sending {Method} to {Uri}", requestMessage.Method, requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, httpCompletionOption, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during SendRequestInternal for {Uri}", requestMessage.RequestUri);
            throw;
        }
    }
}