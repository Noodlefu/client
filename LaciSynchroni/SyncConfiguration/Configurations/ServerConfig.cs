using System;
using System.Text.Json.Serialization;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI;

namespace LaciSynchroni.SyncConfiguration.Configurations;

[Serializable]
public class ServerConfig : ISyncConfiguration
{
    [Obsolete("Use SelectedServerUuid")]
    [JsonPropertyName("CurrentServer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyCurrentServerIndex { get; set; } = 0;

    public Guid SelectedServerUuid { get; set; } = Guid.Empty;

    public List<ServerStorage> ServerStorage { get; set; } = new()
    {
        { new ServerStorage() { ServerName = ApiController.MainServer, ServerUri = ApiController.MainServiceUri, UseOAuth2 = true } },
    };

    public bool SendCensusData { get; set; } = false;
    public bool ShownCensusPopup { get; set; } = false;
    public bool ShowServerPickerInMainMenu { get; set; } = false;
    public bool EnableMultiConnect { get; set; } = true;

    public int Version { get; set; } = 3;
}