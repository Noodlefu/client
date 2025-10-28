using LaciSynchroni.Common.Data;
using System;

namespace LaciSynchroni.PlayerData.Pairs
{
    public record ServerBasedUserKey(UserData UserData, Guid ServerUuid);
}