﻿using System;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.Files;
using LaciSynchroni.Common.Routes;
using LaciSynchroni.FileCache;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.UI;
using LaciSynchroni.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LaciSynchroni.WebAPI.Files;

public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileDbManager;
    private readonly SyncConfigService _syncConfigService;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();

    public FileUploadManager(ILogger<FileUploadManager> logger, SyncMediator mediator,
        SyncConfigService syncConfigService,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileDbManager,
        ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _syncConfigService = syncConfigService;
        _orchestrator = orchestrator;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            ResetForServer(msg.ServerUuid);
        });
    }

    public List<FileTransfer> CurrentUploads { get; } = [];

    public async Task DeleteAllFiles(Guid serverUuid)
    {
        var uri = RequireUriForServer(serverUuid);

        await _orchestrator.SendRequestAsync(serverUuid, HttpMethod.Post, FilesRoutes.ServerFilesDeleteAllFullPath(uri)).ConfigureAwait(false);
    }

    public async Task<List<string>> UploadFiles(Guid serverUuid, List<string> hashesToUpload, IProgress<string> progress, CancellationToken ct)
    {
        Logger.LogDebug("Trying to upload files");
        var filesPresentLocally = hashesToUpload.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);
        var locallyMissingFiles = hashesToUpload.Except(filesPresentLocally, StringComparer.Ordinal).ToList();
        if (locallyMissingFiles.Any())
        {
            return locallyMissingFiles;
        }

        progress.Report($"Starting upload for {filesPresentLocally.Count} files");

        var filesToUpload = await FilesSend(serverUuid, [.. filesPresentLocally], [], ct).ConfigureAwait(false);

        if (filesToUpload.Exists(f => f.IsForbidden))
        {
            return [.. filesToUpload.Where(f => f.IsForbidden).Select(f => f.Hash)];
        }

        Task uploadTask = Task.CompletedTask;
        int i = 1;
        foreach (var file in filesToUpload)
        {
            progress.Report($"Uploading file {i++}/{filesToUpload.Count}. Please wait until the upload is completed.");
            Logger.LogDebug("[{hash}] Compressing", file);
            var data = await _fileDbManager.GetCompressedFileData(file.Hash, ct).ConfigureAwait(false);
            Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1)!.ResolvedFilepath);
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(serverUuid, data.Item2, file.Hash, false, ct);
            ct.ThrowIfCancellationRequested();
        }

        await uploadTask.ConfigureAwait(false);

        return [];
    }

    public async Task<CharacterData> UploadFiles(Guid serverUuid, CharacterData data, List<UserData> visiblePlayers)
    {
        CancelUpload(serverUuid);

        var tokenSource = new CancellationTokenSource();
        if (!_cancellationTokens.TryAdd(serverUuid, tokenSource))
        {
            Logger.LogError("[{ServerUuid}] Failed to add cancellation token, token already present.", serverUuid);
        }
        var uploadToken = tokenSource.Token;
        var server = _serverManager.GetServerByUuid(serverUuid);
        Logger.LogDebug("Sending Character data {Hash} to service {Url}", data.DataHash.Value, server.ServerUri);

        HashSet<string> unverifiedUploads = GetUnverifiedFiles(data);
        if (unverifiedUploads.Any())
        {
            await UploadUnverifiedFiles(serverUuid, unverifiedUploads, visiblePlayers, uploadToken).ConfigureAwait(false);
            Logger.LogInformation("Upload complete for {Hash}", data.DataHash.Value);
        }

        foreach (var kvp in data.FileReplacements)
        {
            data.FileReplacements[kvp.Key].RemoveAll(i => _orchestrator.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
        }

        return data;
    }

    private async Task<List<UploadFileDto>> FilesSend(Guid serverUuid, List<string> hashes, List<string> uids, CancellationToken ct)
    {
        var uri = RequireUriForServer(serverUuid);
        FilesSendDto filesSendDto = new()
        {
            FileHashes = hashes,
            UIDs = uids
        };
        var response = await _orchestrator.SendRequestAsync(serverUuid, HttpMethod.Post, FilesRoutes.ServerFilesFilesSendFullPath(uri), filesSendDto, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private HashSet<string> GetUnverifiedFiles(CharacterData data)
    {
        HashSet<string> unverifiedUploadHashes = new(StringComparer.Ordinal);
        foreach (var item in data.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
            {
                verifiedTime = DateTime.MinValue;
            }

            if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)))
            {
                Logger.LogTrace("Verifying {item}, last verified: {date}", item, verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        return unverifiedUploadHashes;
    }


    private async Task UploadFile(Guid serverUuid, byte[] compressedFile, string fileHash, bool postProgress, CancellationToken uploadToken)
    {
        Logger.LogInformation("[{hash}] Uploading {size}", fileHash, UiSharedService.ByteToString(compressedFile.Length));

        if (uploadToken.IsCancellationRequested) return;

        try
        {
            await UploadFileStream(serverUuid, compressedFile, fileHash, _syncConfigService.Current.UseAlternativeFileUpload, postProgress, uploadToken).ConfigureAwait(false);
            _verifiedUploadedHashes[fileHash] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            if (!_syncConfigService.Current.UseAlternativeFileUpload && ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "[{hash}] Error during file upload, trying alternative file upload", fileHash);
                await UploadFileStream(serverUuid, compressedFile, fileHash, munged: true, postProgress, uploadToken).ConfigureAwait(false);
            }
            else
            {
                Logger.LogWarning(ex, "[{hash}] File upload cancelled", fileHash);
            }
        }
    }

    private async Task UploadFileStream(Guid serverUuid, byte[] compressedFile, string fileHash, bool munged, bool postProgress, CancellationToken uploadToken)
    {
        var uri = RequireUriForServer(serverUuid);
        if (munged)
        {
            FileDownloadManager.MungeBuffer(compressedFile.AsSpan());
        }

        using var ms = new MemoryStream(compressedFile);

        Progress<UploadProgress>? prog = !postProgress ? null : new((prog) =>
        {
            try
            {
                CurrentUploads.Single(f => string.Equals(f.Hash, fileHash, StringComparison.Ordinal) && f.ServerUuid == serverUuid).Transferred = prog.Uploaded;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[{hash}] Could not set upload progress", fileHash);
            }
        });

        var streamContent = new ProgressableStreamContent(ms, prog);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage response;
        if (!munged)
            response = await _orchestrator.SendRequestStreamAsync(serverUuid, HttpMethod.Post, FilesRoutes.ServerFilesUploadFullPath(uri, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        else
            response = await _orchestrator.SendRequestStreamAsync(serverUuid, HttpMethod.Post, FilesRoutes.ServerFilesUploadMunged(uri, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        Logger.LogDebug("[{hash}] Upload Status: {status}", fileHash, response.StatusCode);
    }

    private async Task UploadUnverifiedFiles(Guid serverUuid, HashSet<string> unverifiedUploadHashes, List<UserData> visiblePlayers, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        Logger.LogDebug("Verifying {count} files", unverifiedUploadHashes.Count);
        var filesToUpload = await FilesSend(serverUuid, [.. unverifiedUploadHashes], visiblePlayers.Select(p => p.UID).ToList(), uploadToken).ConfigureAwait(false);

        foreach (var file in filesToUpload.Where(f => !f.IsForbidden).DistinctBy(f => f.Hash))
        {
            try
            {
                CurrentUploads.Add(new UploadFileTransfer(file, serverUuid)
                {
                    Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length,
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Tried to request file {hash} but file was not present", file.Hash);
            }
        }

        foreach (var file in filesToUpload.Where(c => c.IsForbidden))
        {
            if (_orchestrator.ForbiddenTransfers.TrueForAll(f => !string.Equals(f.Hash, file.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new UploadFileTransfer(file, serverUuid)
                {
                    LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty,
                });
            }

            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
        }

        var totalSize = CurrentUploads.Sum(c => c.Total);
        Logger.LogDebug("Compressing and uploading files");
        Task uploadTask = Task.CompletedTask;
        foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
        {
            Logger.LogDebug("[{hash}] Compressing", file);
            var data = await _fileDbManager.GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
            CurrentUploads.Single(e => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal) && e.ServerUuid == serverUuid).Total = data.Item2.Length;
            Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1)!.ResolvedFilepath);
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(serverUuid, data.Item2, file.Hash, true, uploadToken);
            uploadToken.ThrowIfCancellationRequested();
        }

        if (CurrentUploads.Any())
        {
            await uploadTask.ConfigureAwait(false);

            var compressedSize = CurrentUploads.Sum(c => c.Total);
            Logger.LogDebug("Upload complete, compressed {size} to {compressed}", UiSharedService.ByteToString(totalSize), UiSharedService.ByteToString(compressedSize));
        }

        foreach (var file in unverifiedUploadHashes.Where(c => !CurrentUploads.Exists(u => string.Equals(u.Hash, c, StringComparison.Ordinal) && u.ServerUuid == serverUuid)))
        {
            _verifiedUploadedHashes[file] = DateTime.UtcNow;
        }

        CurrentUploads.Clear();
    }

    private void CancelUpload(Guid serverUuid)
    {
        CancelUploadsToServer(serverUuid);
    }

    private void CancelUploadsToServer(Guid serverUuid)
    {
        if (_cancellationTokens.TryRemove(serverUuid, out var token))
        {
            token.Cancel();
            token.Dispose();
        }
        CurrentUploads.RemoveAll(transfer => transfer.ServerUuid == serverUuid);
    }

    private Uri RequireUriForServer(Guid serverUuid)
    {
        var uri = _orchestrator.GetFileCdnUri(serverUuid);
        if (uri == null) throw new InvalidOperationException("FileTransferManager is not initialized");
        return uri;
    }

    private void ResetForServer(Guid serverUuid)
    {
        CancelUploadsToServer(serverUuid);
        _verifiedUploadedHashes.Clear();
    }

    private void Reset()
    {
        _cancellationTokens.Values.ToList().ForEach(c =>
        {
            c.Cancel();
            c.Dispose();
        });
        _cancellationTokens.Clear();
        CurrentUploads.Clear();
        _verifiedUploadedHashes.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Reset();
    }
}