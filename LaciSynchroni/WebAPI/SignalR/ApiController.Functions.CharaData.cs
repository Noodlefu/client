using System;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.CharaData;
using LaciSynchroni.Common.Dto.User;

namespace LaciSynchroni.WebAPI;

public sealed partial class ApiController
{
    public async Task<CharaDataFullDto?> CharaDataCreate(Guid serverUuid)
    {
        return await GetClientForServer(serverUuid)!.CharaDataCreate().ConfigureAwait(false);
    }

    public async Task<CharaDataFullDto?> CharaDataUpdate(Guid serverUuid, CharaDataUpdateDto updateDto)
    {
        return await GetClientForServer(serverUuid)!.CharaDataUpdate(updateDto).ConfigureAwait(false);
    }

    public async Task<bool> CharaDataDelete(Guid serverUuid, string id)
    {
        return await GetClientForServer(serverUuid)!.CharaDataDelete(id).ConfigureAwait(false);
    }

    public async Task<CharaDataMetaInfoDto?> CharaDataGetMetainfo(Guid serverUuid, string id)
    {
        return await GetClientForServer(serverUuid)!.CharaDataGetMetainfo(id).ConfigureAwait(false);
    }

    public async Task<CharaDataFullDto?> CharaDataAttemptRestore(Guid serverUuid, string id)
    {
        return await GetClientForServer(serverUuid)!.CharaDataAttemptRestore(id).ConfigureAwait(false);
    }

    public async Task<List<CharaDataFullDto>> CharaDataGetOwn(Guid serverUuid)
    {
        return await GetClientForServer(serverUuid)!.CharaDataGetOwn().ConfigureAwait(false);
    }

    public async Task<List<CharaDataMetaInfoDto>> CharaDataGetShared(Guid serverUuid)
    {
        return await GetClientForServer(serverUuid)!.CharaDataGetShared().ConfigureAwait(false);
    }

    public async Task<CharaDataDownloadDto?> CharaDataDownload(Guid serverUuid, string id)
    {
        return await GetClientForServer(serverUuid)!.CharaDataDownload(id).ConfigureAwait(false);
    }

    public async Task<string> GposeLobbyCreate(Guid serverUuid)
    {
        return await GetClientForServer(serverUuid)!.GposeLobbyCreate().ConfigureAwait(false);
    }

    public async Task<bool> GposeLobbyLeave(Guid serverUuid)
    {
        return await GetClientForServer(serverUuid)!.GposeLobbyLeave().ConfigureAwait(false);
    }

    public async Task<List<UserData>> GposeLobbyJoin(Guid serverUuid, string lobbyId)
    {
        return await GetClientForServer(serverUuid)!.GposeLobbyJoin(lobbyId).ConfigureAwait(false);
    }

    public async Task GposeLobbyPushCharacterData(Guid serverUuid, CharaDataDownloadDto charaDownloadDto)
    {
        await GetClientForServer(serverUuid)!.GposeLobbyPushCharacterData(charaDownloadDto).ConfigureAwait(false);
    }

    public async Task GposeLobbyPushPoseData(Guid serverUuid, PoseData poseData)
    {
        await GetClientForServer(serverUuid)!.GposeLobbyPushPoseData(poseData).ConfigureAwait(false);
    }

    public async Task GposeLobbyPushWorldData(Guid serverUuid, WorldData worldData)
    {
        await GetClientForServer(serverUuid)!.GposeLobbyPushWorldData(worldData).ConfigureAwait(false);
    }
}
