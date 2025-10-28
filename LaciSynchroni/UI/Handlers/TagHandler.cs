using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Models;

namespace LaciSynchroni.UI.Handlers;

public class TagHandler
{
    public const string CustomAllTag = "Laci_All";
    public const string CustomOfflineTag = "Laci_Offline";
    public const string CustomOfflineSyncshellTag = "Laci_OfflineSyncshell";
    public const string CustomOnlineTag = "Laci_Online";
    public const string CustomUnpairedTag = "Laci_Unpaired";
    public const string CustomVisibleTag = "Laci_Visible";
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public TagHandler(ServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
    }

    public void AddTag(Guid serverUuid, string tag)
    {
        _serverConfigurationManager.AddTag(serverUuid, tag);
    }

    public void AddTagToPairedUid(Guid serverUuid, string uid, string tagName)
    {
        _serverConfigurationManager.AddTagForUid(serverUuid, uid, tagName);
    }

    public List<TagWithServer> GetAllTagsSorted()
    {
        return _serverConfigurationManager.GetServerInfo()
            .SelectMany(server =>
            {
                var tags = _serverConfigurationManager.GetServerAvailablePairTags(server.Id);
                return tags.Select(tag => new TagWithServer(server.Id, tag));
            })
            .OrderBy(t => t.Tag, StringComparer.Ordinal)
            .ToList();
    }

    public List<string> GetAllTagsForServerSorted(Guid serverUuid)
    {
        return _serverConfigurationManager.GetServerAvailablePairTags(serverUuid)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    public HashSet<string> GetOtherUidsForTag(Guid serverUuid, string tag)
    {
        return _serverConfigurationManager.GetUidsForTag(serverUuid, tag);
    }

    public bool HasAnyTag(Guid serverUuid, string uid)
    {
        return _serverConfigurationManager.HasTags(serverUuid, uid);
    }

    public bool HasTag(Guid serverUuid, string uid, string tagName)
    {
        return _serverConfigurationManager.ContainsTag(serverUuid, uid, tagName);
    }

    /// <summary>
    /// Is this tag opened in the paired clients UI?
    /// </summary>
    /// <param name="serverIndex">server the tag belongs to</param>
    /// <param name="tag">the tag</param>
    /// <returns>open true/false</returns>
    public bool IsTagOpen(Guid serverUuid, string tag)
    {
        return _serverConfigurationManager.ContainsOpenPairTag(serverUuid, tag);
    }

    /// <summary>
    /// For tags tag are "global", for example the syncshell grouping folder. These are not actually tags, but internally
    /// used identifiers for UI elements that can be persistently opened/closed
    /// </summary>
    /// <param name="tag"></param>
    /// <returns></returns>
    public bool IsGlobalTagOpen(string tag)
    {
        return _serverConfigurationManager.ContainsGlobalOpenPairTag(tag);
    }

    public void RemoveTag(Guid serverUuid, string tag)
    {
        _serverConfigurationManager.RemoveTag(serverUuid, tag);
    }

    public void RemoveTagFromPairedUid(Guid serverUuid, string uid, string tagName)
    {
        _serverConfigurationManager.RemoveTagForUid(serverUuid, uid, tagName);
    }

    public void ToggleTagOpen(Guid serverUuid, string tag)
    {
        if (IsTagOpen(serverUuid, tag))
        {
            _serverConfigurationManager.RemoveOpenPairTag(serverUuid, tag);
        }
        else
        {
            _serverConfigurationManager.AddOpenPairTag(serverUuid, tag);
        }
    }

    public void ToggleGlobalTagOpen(string tag)
    {
        if (IsGlobalTagOpen(tag))
        {
            _serverConfigurationManager.RemoveOpenGlobalPairTag(tag);
        }
        else
        {
            _serverConfigurationManager.AddGlobalOpenPairTag(tag);
        }
    }
}