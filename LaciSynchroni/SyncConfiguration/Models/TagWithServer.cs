namespace LaciSynchroni.SyncConfiguration.Models;

public record TagWithServer(Guid ServerUuid, string Tag)
{
    public string AsImGuiId()
    {
        return $"{ServerUuid}-${Tag}";
    }
}

