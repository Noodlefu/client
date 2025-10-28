using System;
using Dalamud.Utility;
using System.Collections.Concurrent;

namespace LaciSynchroni.Services
{
    using PlayerNameHash = string;

    public class ConcurrentPairLockService
    {
        private readonly ConcurrentDictionary<PlayerNameHash, Guid> _renderLocks = new(StringComparer.Ordinal);
        private readonly Lock _resourceLock = new();

        public Guid GetRenderLock(PlayerNameHash? playerNameHash, Guid? serverUuid)
        {
            if (serverUuid is null || serverUuid == Guid.Empty || playerNameHash.IsNullOrWhitespace()) return Guid.Empty;

            lock (_resourceLock)
            {
                return _renderLocks.GetOrAdd(playerNameHash, serverUuid.Value);
            }
        }

        public bool ReleaseRenderLock(PlayerNameHash? playerNameHash, Guid? serverUuid)
        {
            if (serverUuid is null || serverUuid == Guid.Empty || playerNameHash.IsNullOrWhitespace()) return false;

            lock (_resourceLock)
            {
                Guid existingServerUuid = _renderLocks.GetValueOrDefault(playerNameHash, Guid.Empty);
                return serverUuid == existingServerUuid && _renderLocks.Remove(playerNameHash, out _);
            }
        }
    }
}