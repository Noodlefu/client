using System;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.User;

namespace LaciSynchroni.WebAPI;

public sealed partial class ApiController
{
    public async Task PushCharacterData(Guid serverUuid, CharacterData data, List<UserData> visibleCharacters)
    {
        await GetClientForServer(serverUuid)!.PushCharacterData(data, visibleCharacters).ConfigureAwait(false);
    }

    public Task UserAddPairToServer(Guid serverUuid, string pairToAdd)
    {
        return UserAddPair(serverUuid, new(new(pairToAdd)));
    }

    public async Task UserAddPair(Guid serverUuid, UserDto user)
    {
        await GetClientForServer(serverUuid)!.UserAddPair(user).ConfigureAwait(false);
    }

    public async Task UserDelete(Guid serverUuid)
    {
        await GetClientForServer(serverUuid)!.UserDelete().ConfigureAwait(false);
    }

    public async Task<UserProfileDto> UserGetProfile(Guid serverUuid, UserDto dto)
    {
        return await GetClientForServer(serverUuid)!.UserGetProfile(dto).ConfigureAwait(false);
    }

    public async Task SetBulkPermissions(Guid serverUuid, BulkPermissionsDto dto)
    {
        await GetClientForServer(serverUuid)!.SetBulkPermissions(dto).ConfigureAwait(false);
    }

    public async Task UserRemovePair(Guid serverUuid, UserDto userDto)
    {
        await GetClientForServer(serverUuid)!.UserRemovePair(userDto).ConfigureAwait(false);
    }

    public async Task UserSetPairPermissions(Guid serverUuid, UserPermissionsDto userPermissions)
    {
        await GetClientForServer(serverUuid)!.UserSetPairPermissions(userPermissions).ConfigureAwait(false);
    }

    public async Task UserSetProfile(Guid serverUuid, UserProfileDto userDescription)
    {
        await GetClientForServer(serverUuid)!.UserSetProfile(userDescription).ConfigureAwait(false);
    }

    public async Task UserUpdateDefaultPermissions(Guid serverUuid, LaciSynchroni.Common.Dto.DefaultPermissionsDto defaultPermissionsDto)
    {
        await GetClientForServer(serverUuid)!.UserUpdateDefaultPermissions(defaultPermissionsDto).ConfigureAwait(false);
    }
}