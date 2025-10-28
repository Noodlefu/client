using Dalamud.Bindings.ImGui;
using LaciSynchroni.Services.ServerConfiguration;

namespace LaciSynchroni.UI.Components
{
    /// <summary>
    /// Quick and dirty server selector that can be used in all places where we need a server selector now, to be replaced with a better
    /// concept down the line
    /// </summary>
    /// <param name="onServerChange">Event called when the selected server changes</param>
    public class ServerSelectorSmall(Action<Guid> onServerChange, Guid currentServerUuid = default)
    {
        private Guid _currentServerUuid = currentServerUuid;
        private readonly Action<Guid> _onServerChange = onServerChange;

        public void Draw(IReadOnlyList<ServerInfoDto> availableServers, IReadOnlyCollection<Guid> connectedServers, float width)
        {
            if (availableServers.Count <= 0 || connectedServers.Count <= 0)
            {
                return;
            }

            if (!connectedServers.Contains(_currentServerUuid))
            {
                var firstConnected = availableServers.FirstOrDefault(server => connectedServers.Contains(server.Id));
                if (firstConnected != null)
                {
                    ChangeSelectedServer(firstConnected.Id);
                }
            }

            var selectedServer = availableServers.FirstOrDefault(server => server.Id == _currentServerUuid) ?? availableServers[0];
            ImGui.SetNextItemWidth(width);
            if (ImGui.BeginCombo("", selectedServer.Name))
            {
                foreach (var server in availableServers)
                {
                    var isSelected = server.Id == _currentServerUuid;
                    var isConnected = connectedServers.Contains(server.Id);
                    if (ImGui.Selectable(server.Name, isSelected, isConnected ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
                    {
                        ChangeSelectedServer(server.Id);
                    }
                    if (!isConnected)
                    {
                        UiSharedService.AttachToolTip($"You are currently not connected to {server.Name} service.");
                    }
                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
        }

        private void ChangeSelectedServer(Guid serverUuid)
        {
            if (_currentServerUuid != serverUuid)
            {
                _currentServerUuid = serverUuid;
                _onServerChange.Invoke(serverUuid);
            }
        }
    }
}