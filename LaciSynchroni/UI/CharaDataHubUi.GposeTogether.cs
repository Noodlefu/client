﻿using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using LaciSynchroni.Services.CharaData.Models;

namespace LaciSynchroni.UI;

internal sealed partial class CharaDataHubUi
{
    private string _joinLobbyId = string.Empty;

    private void DrawGposeControls()
    {
        _uiSharedService.BigText("GPose Actors");
        ImGuiHelpers.ScaledDummy(5);

        if (!_uiSharedService.IsInGpose)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("Gpose actors are only available in GPose.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);

            return;
        }

        using var indent = ImRaii.PushIndent(10f);

        var gposeTarget = _dalamudUtilService.GetGposeTargetGameObjectAsync().GetAwaiter().GetResult();

        foreach (var actor in _dalamudUtilService.GetGposeCharactersFromObjectTable())
        {
            if (actor == null) continue;
            var actorName = string.IsNullOrEmpty(actor.Name.TextValue) ? $"Unknown_{actor.Address}" : actor.Name.TextValue;

            UiSharedService.DrawGrouped(() =>
            {
                using var actorId = ImRaii.PushId($"{actor.Address}_{actorName}");

                if (_uiSharedService.IconButton(FontAwesomeIcon.Crosshairs))
                {
                    unsafe
                    {
                        _dalamudUtilService.GposeTarget = (GameObject*)actor.Address;
                    }
                }
                ImGui.SameLine();
                UiSharedService.AttachToolTip($"Target the GPose Character {actorName}");
                ImGui.AlignTextToFramePadding();
                var pos = ImGui.GetCursorPosX();

                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, actor.Address == (gposeTarget?.Address ?? nint.Zero)))
                {
                    ImGui.TextUnformatted(CharaName(actor.Name.TextValue));
                }
                ImGui.SameLine(250);
                var handled = _charaDataManager.HandledCharaData.FirstOrDefault(c => string.Equals(c.Name, actor.Name.TextValue, StringComparison.Ordinal));
                using (ImRaii.Disabled(handled == null))
                {
                    _uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
                    var id = string.IsNullOrEmpty(handled?.MetaInfo.Uploader.UID) ? handled?.MetaInfo.Id : handled.MetaInfo.FullId;
                    UiSharedService.AttachToolTip($"Applied Data: {id ?? "No data applied"}");

                    ImGui.SameLine();
                    // maybe do this better, check with brio for handled charas or sth
                    using (ImRaii.Disabled(!actor.Name.TextValue.StartsWith("Brio ", StringComparison.Ordinal)))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                        {
                            _charaDataManager.RemoveChara(actor.Name.TextValue);
                        }
                        UiSharedService.AttachToolTip($"Remove character {actorName}");
                    }
                    ImGui.SameLine();
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
                    {
                        _charaDataManager.RevertChara(handled);
                    }
                    UiSharedService.AttachToolTip($"Revert applied data from {actorName}");
                    ImGui.SetCursorPosX(pos);
                    DrawPoseData(handled?.MetaInfo, actor.Name.TextValue, true);
                }
            });

            ImGuiHelpers.ScaledDummy(2);
        }
    }

    private void DrawGposeTogether()
    {
        if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("BRIO IS NEEDED FOR GPOSE TOGETHER.", ImGuiColors.DalamudRed);
            ImGuiHelpers.ScaledDummy(5);
        }

        if (!_apiController.ConnectedServerUuids.Any(p => p == _selectedServerUuid))
        {
            _selectedServerUuid = _apiController.ConnectedServerUuids.FirstOrDefault();
        }

        bool isServerConnected = _uiSharedService.ApiController.IsServerConnected(_selectedServerUuid);
        if (!isServerConnected)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("CANNOT USE GPOSE TOGETHER WHILE DISCONNECTED FROM THE SERVER.", ImGuiColors.DalamudRed);
            ImGuiHelpers.ScaledDummy(5);
        }

        _uiSharedService.BigText("GPose Together");
        DrawHelpFoldout("GPose Together is a way to do multiplayer GPose sessions and collaborations." + UiSharedService.DoubleNewLine
            + "GPose together requires Brio to function. Only Brio is also supported for the actual posing interactions. Attempting to pose using other tools will lead to conflicts and exploding characters." + UiSharedService.DoubleNewLine
            + "To use GPose together you either create or join a GPose Together Lobby. After you and other people have joined, make sure that everyone is on the same map. "
            + "It is not required for you to be on the same server, DC or instance. Users that are on the same map will be drawn as moving purple wisps in the overworld, so you can easily find each other." + UiSharedService.DoubleNewLine
            + "Once you are close to each other you can initiate GPose. You must either assign or spawn characters for each of the lobby users. Their own poses and positions to their character will be automatically applied." + Environment.NewLine
            + "Pose and location data during GPose are updated approximately every 10-20s.");

        if (!_charaDataManager.BrioAvailable || !isServerConnected)
        {
            return;
        }

        UiSharedService.DistanceSeparator();
        _uiSharedService.BigText("Lobby Controls");
        if (string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create New GPose Together Lobby"))
            {
                _charaDataGposeTogetherManager.CreateNewLobby(_selectedServerUuid);
            }
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.ScaledNextItemWidth(250);
            ImGui.InputTextWithHint("##lobbyId", "GPose Lobby Id", ref _joinLobbyId, 30);
            ImGui.SameLine();

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Join GPose Together Lobby"))
            {
                _charaDataGposeTogetherManager.JoinGPoseLobby(_selectedServerUuid, _joinLobbyId);
                _joinLobbyId = string.Empty;
            }
            if (!string.IsNullOrEmpty(_charaDataGposeTogetherManager.LastGPoseLobbyId)
                && _uiSharedService.IconTextButton(FontAwesomeIcon.LongArrowAltRight, $"Rejoin Last Lobby {_charaDataGposeTogetherManager.LastGPoseLobbyId}"))
            {
                _charaDataGposeTogetherManager.JoinGPoseLobby(_selectedServerUuid, _charaDataGposeTogetherManager.LastGPoseLobbyId);
            }
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("GPose Lobby");
            ImGui.SameLine();
            UiSharedService.ColorTextWrapped(_charaDataGposeTogetherManager.CurrentGPoseLobbyId, ImGuiColors.ParsedGreen);            
            ImGui.SameLine();
            if (_uiSharedService.IconButton(FontAwesomeIcon.Clipboard))
            {
                ImGui.SetClipboardText(_charaDataGposeTogetherManager.CurrentGPoseLobbyId);
            }
            UiSharedService.AttachToolTip("Copy Lobby ID to clipboard.");
            if (_charaDataGposeTogetherManager.CurrentGPoseLobbyServerId.HasValue)
            {
                ImGui.TextUnformatted("Current Server: ");
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped($"{_apiController.GetServerName(_charaDataGposeTogetherManager.CurrentGPoseLobbyServerId.Value)}", ImGuiColors.ParsedGreen);
            }
            using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowLeft, "Leave GPose Lobby"))
                {
                    _charaDataGposeTogetherManager.LeaveGPoseLobby(_selectedServerUuid);
                }
            }
            UiSharedService.AttachToolTip("Leave the current GPose lobby." + UiSharedService.TooltipSeparator + "Hold CTRL and click to leave.");
        }

        UiSharedService.DistanceSeparator();

        if (!_uiSharedService.IsInGpose)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("Assigning users to characters is only available in GPose.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);
        }
        else if(string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("Create or Join a Gpose Together lobby to assign characters.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);
        }
        else
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowUp, "Send Updated Character Data"))
            {
                _ = _charaDataGposeTogetherManager.PushCharacterDownloadDto(_selectedServerUuid);
            }
            UiSharedService.AttachToolTip("This will send your current appearance, pose and world data to all users in the lobby.");

            UiSharedService.DistanceSeparator();
            ImGui.TextUnformatted("Users In Lobby");
            var gposeCharas = _dalamudUtilService.GetGposeCharactersFromObjectTable();
            var self = _dalamudUtilService.GetPlayerCharacter();
            gposeCharas = gposeCharas.Where(c => c != null && !string.Equals(c.Name.TextValue, self.Name.TextValue, StringComparison.Ordinal)).ToList();

            using (ImRaii.Child("charaChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiHelpers.ScaledDummy(3);

                if (!_charaDataGposeTogetherManager.UsersInLobby.Any() && !string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
                {
                    UiSharedService.DrawGroupedCenteredColorText("No other users in current GPose lobby", ImGuiColors.DalamudYellow);
                }
                else
                {
                    foreach (var user in _charaDataGposeTogetherManager.UsersInLobby)
                    {
                        DrawLobbyUser(user, gposeCharas);
                    }
                }
            }
        }
    }

    private void DrawLobbyUser(GposeLobbyUserData user,
        IEnumerable<ICharacter?> gposeCharas)
    {
        using var id = ImRaii.PushId(user.UserData.UID);
        using var indent = ImRaii.PushIndent(5f);
        var sameMapAndServer = _charaDataGposeTogetherManager.IsOnSameMapAndServer(user);
        var width = ImGui.GetContentRegionAvail().X - 5;
        UiSharedService.DrawGrouped(() =>
        {
            var availWidth = ImGui.GetContentRegionAvail().X;
            ImGui.AlignTextToFramePadding();
            var note = _serverConfigurationManager.GetNoteForUid(_selectedServerUuid, user.UserData.UID);
            var userText = note == null ? user.UserData.AliasOrUID : $"{note} ({user.UserData.AliasOrUID})";
            UiSharedService.ColorText(userText, ImGuiColors.ParsedGreen);

            var buttonsize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X;
            var buttonsize2 = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(availWidth - (buttonsize + buttonsize2 + ImGui.GetStyle().ItemSpacing.X));
            using (ImRaii.Disabled(!_uiSharedService.IsInGpose || user.CharaData == null || user.Address == nint.Zero))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                {
                    _ = _charaDataGposeTogetherManager.ApplyCharaData(_selectedServerUuid, user);
                }
            }
            UiSharedService.AttachToolTip("Apply newly received character data to selected actor." + UiSharedService.TooltipSeparator + "Note: If the button is grayed out, the latest data has already been applied.");
            ImGui.SameLine();
            using (ImRaii.Disabled(!_uiSharedService.IsInGpose || user.CharaData == null || sameMapAndServer.SameEverything))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                {
                    _ = _charaDataGposeTogetherManager.SpawnAndApplyData(_selectedServerUuid, user);
                }
            }
            UiSharedService.AttachToolTip("Spawn new actor, apply character data and and assign it to this user." + UiSharedService.TooltipSeparator + "Note: If the button is grayed out, " +
                "the user has not sent any character data or you are on the same map, server and instance. If the latter is the case, join a group with that user and assign the character to them.");


            using (ImRaii.Group())
            {
                UiSharedService.ColorText("Map Info", ImGuiColors.DalamudGrey);
                ImGui.SameLine();
                _uiSharedService.IconText(FontAwesomeIcon.ExternalLinkSquareAlt, ImGuiColors.DalamudGrey);
            }
            UiSharedService.AttachToolTip(user.WorldDataDescriptor + UiSharedService.TooltipSeparator);

            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.Map, sameMapAndServer.SameMap ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && user.WorldData != null)
            {
                _dalamudUtilService.SetMarkerAndOpenMap(new(user.WorldData.Value.PositionX, user.WorldData.Value.PositionY, user.WorldData.Value.PositionZ), user.Map);
            }
            UiSharedService.AttachToolTip((sameMapAndServer.SameMap ? "You are on the same map." : "You are not on the same map.") + UiSharedService.TooltipSeparator
                + "Note: Click to open the users location on your map." + Environment.NewLine
                + "Note: For GPose synchronization to work properly, you must be on the same map.");

            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.Globe, sameMapAndServer.SameServer ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
            UiSharedService.AttachToolTip((sameMapAndServer.SameMap ? "You are on the same server." : "You are not on the same server.") + UiSharedService.TooltipSeparator
                + "Note: GPose synchronization is not dependent on the current server, but you will have to spawn a character for the other lobby users.");

            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.Running, sameMapAndServer.SameEverything ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
            UiSharedService.AttachToolTip(sameMapAndServer.SameEverything ? "You are in the same instanced area." : "You are not the same instanced area." + UiSharedService.TooltipSeparator +
                "Note: Users not in your instance, but on the same map, will be drawn as floating wisps." + Environment.NewLine
                + "Note: GPose synchronization is not dependent on the current instance, but you will have to spawn a character for the other lobby users.");

            using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
            {
                UiSharedService.ScaledNextItemWidth(200);
                using (var combo = ImRaii.Combo("##character", string.IsNullOrEmpty(user.AssociatedCharaName) ? "No character assigned" : CharaName(user.AssociatedCharaName)))
                {
                    if (combo)
                    {
                        foreach (var chara in gposeCharas)
                        {
                            if (chara == null) continue;

                            if (ImGui.Selectable(CharaName(chara.Name.TextValue), chara.Address == user.Address))
                            {
                                user.AssociatedCharaName = chara.Name.TextValue;
                                user.Address = chara.Address;
                            }
                        }
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(user.Address == nint.Zero))
                {
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                    {
                        user.AssociatedCharaName = string.Empty;
                        user.Address = nint.Zero;
                    }
                }
                UiSharedService.AttachToolTip("Unassign Actor for this user");
                if (_uiSharedService.IsInGpose && user.Address == nint.Zero)
                {
                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudRed);
                    UiSharedService.AttachToolTip("No valid character assigned for this user. Pose data will not be applied.");
                }
            }
        }, 5, width);
        ImGuiHelpers.ScaledDummy(5);
    }
}
