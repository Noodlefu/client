﻿using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LaciSynchroni.Common.Data.Enum;
using LaciSynchroni.Common.Data.Extensions;
using LaciSynchroni.Common.Dto.Group;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Numerics;

namespace LaciSynchroni.UI.Components.Popup;

public class SyncshellAdminUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly bool _isModerator = false;
    private readonly bool _isOwner = false;
    private readonly Guid _serverUuid;
    private readonly string _serverName;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private int _multiInvites;
    private string _newPassword;
    private bool _pwChangeSuccess;
    private Task<int>? _pruneTestTask;
    private Task<int>? _pruneTask;
    private int _pruneDays = 14;

    public SyncshellAdminUI(ILogger<SyncshellAdminUI> logger, SyncMediator mediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, GroupFullInfoDto groupFullInfo, PerformanceCollectorService performanceCollectorService, Guid serverUuid)
        : base(logger, mediator, "###LaciSynchroniMainUI", performanceCollectorService)
    {
        GroupFullInfo = groupFullInfo;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _isOwner = string.Equals(GroupFullInfo.OwnerUID, _apiController.GetUidByServer(serverUuid), StringComparison.Ordinal);
        _isModerator = GroupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _multiInvites = 30;
        _pwChangeSuccess = true;
        _serverUuid = serverUuid;
        _serverName = _apiController.GetServerName(serverUuid);
        IsOpen = true;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(700, 500),
            MaximumSize = new(900, 2000),
        };

        WindowName = $"Syncshell Admin Panel ({groupFullInfo.GroupAliasOrGID} @ {_serverName})";
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    protected override void DrawInternal()
    {
        var isServerConnected = _apiController.IsServerConnected(_serverUuid);
        if (!isServerConnected)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText($"You are currently not connected to {_serverName}.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);
        }

        if (!_isModerator && !_isOwner) return;

        var key = new ServerBasedGroupKey(GroupFullInfo.Group, _serverUuid);
        GroupFullInfo = _pairManager.Groups[key];

        using var id = ImRaii.PushId("syncshell_admin_" + GroupFullInfo.GID);

        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted(GroupFullInfo.GroupAliasOrGID + " Administrative Panel");

        ImGui.Separator();
        var perm = GroupFullInfo.GroupPermissions;

        using (ImRaii.Disabled(!isServerConnected))
        {
            using var tabbar = ImRaii.TabBar("syncshell_tab_" + GroupFullInfo.GID);
            if (tabbar)
            {
                var inviteTab = ImRaii.TabItem("Invites");
                if (inviteTab)
                {
                    bool isInvitesDisabled = perm.IsDisableInvites();

                    if (_uiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                        isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
                    {
                        perm.SetDisableInvites(!isInvitesDisabled);
                        _ = _apiController.GroupChangeGroupPermissionState(_serverUuid, new(GroupFullInfo.Group, perm));
                    }

                    ImGuiHelpers.ScaledDummy(2f);

                    UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
                    {
                        ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(_serverUuid, new(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
                    }
                    UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
                    ImGui.InputInt("##amountofinvites", ref _multiInvites);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
                        {
                            _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(_serverUuid, new(GroupFullInfo.Group), _multiInvites).Result);
                        }
                    }

                    if (_oneTimeInvites.Any())
                    {
                        var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                        ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
                        {
                            ImGui.SetClipboardText(invites);
                        }
                    }
                }
                inviteTab.Dispose();

                var mgmtTab = ImRaii.TabItem("User Management");
                if (mgmtTab)
                {
                    var userNode = ImRaii.TreeNode("User List & Administration");
                    if (userNode)
                    {
                        var pairs = _pairManager.GroupPairs
                            .FirstOrDefault(pair => pair.Key.ServerUuid == _serverUuid && string.Equals(pair.Key.GroupFullInfo.GID, GroupFullInfo.GID, StringComparison.Ordinal)).Value;
                        if (pairs.Count <= 0)
                        {
                            UiSharedService.ColorTextWrapped("No users found in this Syncshell", ImGuiColors.DalamudYellow);
                        }
                        else
                        {
                            using var table = ImRaii.Table("userList#" + GroupFullInfo.Group.GID, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, CalculateTableHeight(pairs.Count));
                            if (table)
                            {
                                ImGui.TableSetupColumn("Alias/UID/Note", ImGuiTableColumnFlags.None, 3);
                                ImGui.TableSetupColumn("Online/Name", ImGuiTableColumnFlags.None, 2);
                                ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.None, 1);
                                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 2);
                                ImGui.TableHeadersRow();

                                var groupedPairs = new Dictionary<Pair, GroupPairUserInfo?>(pairs.Select(p => new KeyValuePair<Pair, GroupPairUserInfo?>(p,
                                    GroupFullInfo.GroupPairUserInfos.TryGetValue(p.UserData.UID, out GroupPairUserInfo value) ? value : null)));

                                foreach (var pair in groupedPairs
                                    .OrderBy(p => !string.Equals(p.Key.UserData.UID, GroupFullInfo.OwnerUID, StringComparison.Ordinal))
                                    .ThenBy(p => !(p.Value?.IsModerator() ?? false))
                                    .ThenBy(p => !(p.Value?.IsPinned() ?? false))
                                    .ThenBy(p => p.Key.GetNote() ?? p.Key.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase))
                                {
                                    var isPairSyncshellOwner = string.Equals(pair.Key.UserData.UID, GroupFullInfo.OwnerUID, StringComparison.Ordinal);
                                    using var tableId = ImRaii.PushId("userTable_" + pair.Key.UserData.UID);

                                    ImGui.TableNextColumn(); // alias/uid/note
                                    var note = pair.Key.GetNote();
                                    var text = note == null ? pair.Key.UserData.AliasOrUID : note + " (" + pair.Key.UserData.AliasOrUID + ")";
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextUnformatted(text);

                                    ImGui.TableNextColumn(); // online/name
                                    string onlineText = pair.Key.IsOnline ? "Online" : "Offline";
                                    if (!string.IsNullOrEmpty(pair.Key.PlayerName))
                                    {
                                        onlineText += " (" + pair.Key.PlayerName + ")";
                                    }
                                    var boolcolor = UiSharedService.GetBoolColor(pair.Key.IsOnline);
                                    ImGui.AlignTextToFramePadding();
                                    UiSharedService.ColorText(onlineText, boolcolor);

                                    ImGui.TableNextColumn(); // special flags
                                    if (pair.Value != null && (isPairSyncshellOwner || pair.Value.Value.IsModerator() || pair.Value.Value.IsPinned()))
                                    {
                                        if (isPairSyncshellOwner)
                                        {
                                            _uiSharedService.IconText(FontAwesomeIcon.Crown);
                                            UiSharedService.AttachToolTip("Owner");
                                        }
                                        else if (pair.Value.Value.IsModerator())
                                        {
                                            _uiSharedService.IconText(FontAwesomeIcon.UserShield);
                                            UiSharedService.AttachToolTip("Moderator");
                                        }
                                        else if (pair.Value.Value.IsPinned())
                                        {
                                            _uiSharedService.IconText(FontAwesomeIcon.Thumbtack);
                                            UiSharedService.AttachToolTip("Pinned");
                                        }
                                    }
                                    else
                                    {
                                        _uiSharedService.IconText(FontAwesomeIcon.None);
                                    }

                                    ImGui.TableNextColumn(); // actions
                                    if (_isOwner)
                                    {
                                        using (ImRaii.Disabled(isPairSyncshellOwner))
                                        {
                                            if (_uiSharedService.IconButton(FontAwesomeIcon.UserShield))
                                            {
                                                GroupPairUserInfo userInfo = pair.Value ?? GroupPairUserInfo.None;

                                                userInfo.SetModerator(!userInfo.IsModerator());

                                                _ = _apiController.GroupSetUserInfo(_serverUuid, new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                                            }
                                        }
                                        UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsModerator() ? "Demod user" : "Mod user");
                                        ImGui.SameLine(0, 2f);
                                    }

                                    if (_isOwner || (pair.Value == null || (pair.Value != null && !pair.Value.Value.IsModerator())))
                                    {
                                        using (ImRaii.Disabled(isPairSyncshellOwner))
                                        {
                                            if (_uiSharedService.IconButton(FontAwesomeIcon.Thumbtack))
                                            {
                                                GroupPairUserInfo userInfo = pair.Value ?? GroupPairUserInfo.None;

                                                userInfo.SetPinned(!userInfo.IsPinned());

                                                _ = _apiController.GroupSetUserInfo(_serverUuid, new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                                            }
                                        }
                                        UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsPinned() ? "Unpin user" : "Pin user");
                                        ImGui.SameLine(0, 2f);

                                        using (ImRaii.Disabled(isPairSyncshellOwner || !UiSharedService.CtrlPressed()))
                                        {
                                            if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                                            {
                                                _ = _apiController.GroupRemoveUser(_serverUuid, new GroupPairDto(GroupFullInfo.Group, pair.Key.UserData));
                                            }
                                        }
                                        UiSharedService.AttachToolTip("Remove user from Syncshell"
                                            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

                                        ImGui.SameLine(0, 2f);
                                        using (ImRaii.Disabled(isPairSyncshellOwner || !UiSharedService.CtrlPressed()))
                                        {
                                            if (_uiSharedService.IconButton(FontAwesomeIcon.Ban))
                                            {
                                                Mediator.Publish(new OpenBanUserPopupMessage(pair.Key, GroupFullInfo));
                                            }
                                        }
                                        UiSharedService.AttachToolTip("Ban user from Syncshell"
                                            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
                                    }
                                    if (_isOwner && (pair.Value != null && pair.Value.Value.IsModerator() && !isPairSyncshellOwner))
                                    {
                                        ImGui.SameLine(0, 2f);
                                        using (ImRaii.Disabled(isPairSyncshellOwner || !UiSharedService.CtrlPressed() || !UiSharedService.ShiftPressed()))
                                        {
                                            if (_uiSharedService.IconButton(FontAwesomeIcon.Crown) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                                            {
                                                _ = _apiController.GroupChangeOwnership(_serverUuid, new(GroupFullInfo.Group, pair.Key.UserData));
                                            }
                                        }

                                        UiSharedService.AttachToolTip($"Transfer ownership of this Syncshell to {pair.Key.PlayerName}?" +
                                            UiSharedService.TooltipSeparator +
                                            "Hold CTRL and SHIFT and click to enable this button" +
                                            UiSharedService.TooltipSeparator +
                                            "WARNING: This action is irreversible");

                                    }
                                }
                            }
                        }
                    }
                    userNode.Dispose();
                    var clearNode = ImRaii.TreeNode("Mass Cleanup");
                    if (clearNode)
                    {
                        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
                            {
                                _ = _apiController.GroupClear(_serverUuid, new(GroupFullInfo.Group));
                            }
                        }
                        UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell."
                            + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

                        ImGuiHelpers.ScaledDummy(2f);
                        ImGui.Separator();
                        ImGuiHelpers.ScaledDummy(2f);

                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Unlink, "Check for Inactive Users"))
                        {
                            _pruneTestTask = _apiController.GroupPrune(_serverUuid, new(GroupFullInfo.Group), _pruneDays, execute: false);
                            _pruneTask = null;
                        }
                        UiSharedService.AttachToolTip($"This will start the prune process for this Syncshell of inactive users that have not logged in in the past {_pruneDays} days."
                            + Environment.NewLine + "You will be able to review the amount of inactive users before executing the prune."
                            + UiSharedService.TooltipSeparator + "Note: this check excludes pinned users and moderators of this Syncshell.");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(150);
                        _uiSharedService.DrawCombo("Days of inactivity", [7, 14, 30, 90], count => count + " days",
                        selected =>
                        {
                            _pruneDays = selected;
                            _pruneTestTask = null;
                            _pruneTask = null;
                        },
                        _pruneDays);

                        if (_pruneTestTask != null)
                        {
                            if (!_pruneTestTask.IsCompleted)
                            {
                                UiSharedService.ColorTextWrapped("Calculating inactive users...", ImGuiColors.DalamudYellow);
                            }
                            else
                            {
                                ImGui.AlignTextToFramePadding();
                                UiSharedService.TextWrapped($"Found {_pruneTestTask.Result} user(s) that have not logged into the {_serverName} server in the past {_pruneDays} days.");
                                if (_pruneTestTask.Result > 0)
                                {
                                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                    {
                                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Prune Inactive Users"))
                                        {
                                            _pruneTask = _apiController.GroupPrune(_serverUuid, new(GroupFullInfo.Group), _pruneDays, execute: true);
                                            _pruneTestTask = null;
                                        }
                                    }
                                    UiSharedService.AttachToolTip($"Pruning will remove {_pruneTestTask?.Result ?? 0} inactive user(s)."
                                        + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
                                }
                            }
                        }
                        if (_pruneTask != null)
                        {
                            if (!_pruneTask.IsCompleted)
                            {
                                UiSharedService.ColorTextWrapped("Pruning Syncshell...", ImGuiColors.DalamudYellow);
                            }
                            else
                            {
                                UiSharedService.TextWrapped($"Syncshell was pruned and {_pruneTask.Result} inactive user(s) have been removed.");
                            }
                        }
                    }
                    clearNode.Dispose();

                    var banNode = ImRaii.TreeNode("User Bans");
                    if (banNode)
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
                        {
                            _bannedUsers = _apiController.GroupGetBannedUsers(_serverUuid, new GroupDto(GroupFullInfo.Group)).Result;
                        }

                        if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp, CalculateTableHeight(_bannedUsers.Count)))
                        {
                            ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
                            ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

                            ImGui.TableHeadersRow();

                            foreach (var bannedUser in _bannedUsers.ToList())
                            {
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(bannedUser.UID);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(bannedUser.BannedBy);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                                ImGui.TableNextColumn();
                                UiSharedService.TextWrapped(bannedUser.Reason);
                                ImGui.TableNextColumn();
                                using var banId = ImRaii.PushId(bannedUser.UID);
                                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban"))
                                {
                                    _ = _apiController.GroupUnbanUser(_serverUuid, bannedUser);
                                    _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                                }
                            }

                            ImGui.EndTable();
                        }
                    }
                    banNode.Dispose();
                }
                mgmtTab.Dispose();

                var permissionTab = ImRaii.TabItem("Permissions");
                if (permissionTab)
                {
                    bool isDisableAnimations = perm.IsPreferDisableAnimations();
                    bool isDisableSounds = perm.IsPreferDisableSounds();
                    bool isDisableVfx = perm.IsPreferDisableVFX();

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Suggest Sound Sync");
                    _uiSharedService.BooleanToColoredIcon(!isDisableSounds);
                    ImGui.SameLine(230);
                    if (_uiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute,
                        isDisableSounds ? "Suggest to enable sound sync" : "Suggest to disable sound sync"))
                    {
                        perm.SetPreferDisableSounds(!perm.IsPreferDisableSounds());
                        _ = _apiController.GroupChangeGroupPermissionState(_serverUuid, new(GroupFullInfo.Group, perm));
                    }

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Suggest Animation Sync");
                    _uiSharedService.BooleanToColoredIcon(!isDisableAnimations);
                    ImGui.SameLine(230);
                    if (_uiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop,
                        isDisableAnimations ? "Suggest to enable animation sync" : "Suggest to disable animation sync"))
                    {
                        perm.SetPreferDisableAnimations(!perm.IsPreferDisableAnimations());
                        _ = _apiController.GroupChangeGroupPermissionState(_serverUuid, new(GroupFullInfo.Group, perm));
                    }

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Suggest VFX Sync");
                    _uiSharedService.BooleanToColoredIcon(!isDisableVfx);
                    ImGui.SameLine(230);
                    if (_uiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle,
                        isDisableVfx ? "Suggest to enable vfx sync" : "Suggest to disable vfx sync"))
                    {
                        perm.SetPreferDisableVFX(!perm.IsPreferDisableVFX());
                        _ = _apiController.GroupChangeGroupPermissionState(_serverUuid, new(GroupFullInfo.Group, perm));
                    }

                    UiSharedService.TextWrapped("Note: those suggested permissions will be shown to users on joining the Syncshell.");
                }
                permissionTab.Dispose();

                if (_isOwner)
                {
                    var ownerTab = ImRaii.TabItem("Owner Settings");
                    if (ownerTab)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("New Password");
                        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Passport, "Change Password");
                        var textSize = ImGui.CalcTextSize("New Password").X;
                        var spacing = ImGui.GetStyle().ItemSpacing.X;

                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(availableWidth - buttonSize - textSize - spacing * 2);
                        ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
                        ImGui.SameLine();
                        using (ImRaii.Disabled(_newPassword.Length < 10))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                            {
                                _pwChangeSuccess = _apiController.GroupChangePassword(_serverUuid, new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
                                _newPassword = string.Empty;
                            }
                        }
                        UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");

                        if (!_pwChangeSuccess)
                        {
                            UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
                        }

                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                        {
                            IsOpen = false;
                            _ = _apiController.GroupDelete(_serverUuid, new(GroupFullInfo.Group));
                        }
                        UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                    }
                    ownerTab.Dispose();
                }
            }
        }
    }

    private static Vector2 CalculateTableHeight(int rowCount)
    {
        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        float headerHeight = ImGui.GetTextLineHeightWithSpacing();
        float desiredHeight = headerHeight + (rowCount * rowHeight) + 10;
        var vectorHeight = new Vector2(-1, desiredHeight);
        return vectorHeight;
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}