using LaciSynchroni.Common.Data;
using System;

namespace LaciSynchroni.PlayerData.Pairs
{
    public record ServerBasedGroupKey(GroupData GroupData, Guid ServerUuid);
}