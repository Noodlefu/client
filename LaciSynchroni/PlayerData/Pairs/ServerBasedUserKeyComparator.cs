namespace LaciSynchroni.PlayerData.Pairs;

public class ServerBasedUserKeyComparator : IEqualityComparer<ServerBasedUserKey>
{
    private ServerBasedUserKeyComparator()
    { }

    public static ServerBasedUserKeyComparator Instance { get; } = new();

    public bool Equals(ServerBasedUserKey? x, ServerBasedUserKey? y)
    {
        if (x == null || y == null) return false;
        return x.UserData.UID.Equals(y.UserData.UID, StringComparison.Ordinal) && x.ServerUuid == y.ServerUuid;
    }

    public int GetHashCode(ServerBasedUserKey obj)
    {
        HashCode hashCode = new();
        hashCode.Add(obj.UserData.UID);
        hashCode.Add(obj.ServerUuid);
        return hashCode.ToHashCode();
    }
}