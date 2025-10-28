namespace LaciSynchroni.PlayerData.Pairs;

public class ServerBasedGroupKeyComparator : IEqualityComparer<ServerBasedGroupKey>
{
    private ServerBasedGroupKeyComparator()
    { }

    public static ServerBasedGroupKeyComparator Instance { get; } = new();

    public bool Equals(ServerBasedGroupKey? x, ServerBasedGroupKey? y)
    {
        if (x == null || y == null) return false;
        return x.GroupData.GID.Equals(y.GroupData.GID, StringComparison.Ordinal) && x.ServerUuid == y.ServerUuid;
    }

    public int GetHashCode(ServerBasedGroupKey obj)
    {
        HashCode hashCode = new();
        hashCode.Add(obj.GroupData.GID);
        hashCode.Add(obj.ServerUuid);
        return hashCode.ToHashCode();
    }
}