using System;
using LaciSynchroni.Common.Dto.Group;

namespace LaciSynchroni.WebAPI;

public sealed partial class ApiController
{
    public async Task GroupBanUser(Guid serverUuid, GroupPairDto dto, string reason)
    {
        await GetClientForServer(serverUuid)!.GroupBanUser(dto, reason).ConfigureAwait(false);
    }

    public async Task GroupChangeGroupPermissionState(Guid serverUuid, GroupPermissionDto dto)
    {
        await GetClientForServer(serverUuid)!.GroupChangeGroupPermissionState(dto).ConfigureAwait(false);
    }

    public async Task GroupChangeIndividualPermissionState(Guid serverUuid, GroupPairUserPermissionDto dto)
    {
        await GetClientForServer(serverUuid)!.GroupChangeIndividualPermissionState(dto).ConfigureAwait(false);
    }

    public async Task GroupChangeOwnership(Guid serverUuid, GroupPairDto groupPair)
    {
        await GetClientForServer(serverUuid)!.GroupChangeOwnership(groupPair).ConfigureAwait(false);
    }

    public async Task<bool> GroupChangePassword(Guid serverUuid, GroupPasswordDto groupPassword)
    {
        return await GetClientForServer(serverUuid)!.GroupChangePassword(groupPassword).ConfigureAwait(false);
    }

    public async Task GroupClear(Guid serverUuid, GroupDto group)
    {
        await GetClientForServer(serverUuid)!.GroupClear(group).ConfigureAwait(false);
    }

    public async Task<GroupJoinDto> GroupCreate(Guid serverUuid)
    {
        return await GetClientForServer(serverUuid)!.GroupCreate().ConfigureAwait(false);
    }

    public async Task<List<string>> GroupCreateTempInvite(Guid serverUuid, GroupDto group, int amount)
    {
        return await GetClientForServer(serverUuid)!.GroupCreateTempInvite(group, amount).ConfigureAwait(false);
    }

    public async Task GroupDelete(Guid serverUuid, GroupDto group)
    {
        await GetClientForServer(serverUuid)!.GroupDelete(group).ConfigureAwait(false);
    }

    public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(Guid serverUuid, GroupDto group)
    {
        return await GetClientForServer(serverUuid)!.GroupGetBannedUsers(group).ConfigureAwait(false);
    }

    public Task<GroupJoinInfoDto> GroupJoinForServer(Guid serverUuid, GroupPasswordDto passwordedGroup)
    {
        return GetClientForServer(serverUuid)!.GroupJoinForServer(passwordedGroup);
    }

    public async Task<GroupJoinInfoDto> GroupJoin(Guid serverUuid, GroupPasswordDto passwordedGroup)
    {
        return await GetClientForServer(serverUuid)!.GroupJoin(passwordedGroup).ConfigureAwait(false);
    }

    public Task<bool> GroupJoinFinalizeForServer(Guid serverUuid, GroupJoinDto passwordedGroup)
    {
        return GetClientForServer(serverUuid)!.GroupJoinFinalizeForServer(passwordedGroup);
    }

    public async Task<bool> GroupJoinFinalize(Guid serverUuid, GroupJoinDto passwordedGroup)
    {
        return await GetClientForServer(serverUuid)!.GroupJoinFinalize(passwordedGroup).ConfigureAwait(false);
    }

    public async Task GroupLeave(Guid serverUuid, GroupDto group)
    {
        await GetClientForServer(serverUuid)!.GroupLeave(group).ConfigureAwait(false);
    }

    public async Task GroupRemoveUser(Guid serverUuid, GroupPairDto groupPair)
    {
        await GetClientForServer(serverUuid)!.GroupRemoveUser(groupPair).ConfigureAwait(false);
    }

    public async Task GroupSetUserInfo(Guid serverUuid, GroupPairUserInfoDto groupPair)
    {
        await GetClientForServer(serverUuid)!.GroupSetUserInfo(groupPair).ConfigureAwait(false);
    }

    public async Task<int> GroupPrune(Guid serverUuid, GroupDto group, int days, bool execute)
    {
        return await GetClientForServer(serverUuid)!.GroupPrune(group, days, execute).ConfigureAwait(false);
    }

    public async Task GroupUnbanUser(Guid serverUuid, GroupPairDto groupPair)
    {
        await GetClientForServer(serverUuid)!.GroupUnbanUser(groupPair).ConfigureAwait(false);
    }
}