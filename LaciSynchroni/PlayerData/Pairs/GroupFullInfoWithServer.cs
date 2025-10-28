using System;
using LaciSynchroni.Common.Dto.Group;

namespace LaciSynchroni.PlayerData.Pairs
{
    public record GroupFullInfoWithServer(Guid ServerUuid, GroupFullInfoDto GroupFullInfo)
    {
    }
}