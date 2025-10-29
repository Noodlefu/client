﻿using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using LaciSynchroni.PlayerData.Pairs;
using System.Collections.Immutable;
using System.Numerics;

namespace LaciSynchroni.UI.Components;

public abstract class DrawFolderBase : IDrawFolder
{
    public IImmutableList<DrawUserPair> DrawPairs { get; init; }
    protected readonly IImmutableList<Pair> _allPairs;
    protected readonly UiSharedService _uiSharedService;
    private float _menuWidth = -1;
    public int OnlinePairs => DrawPairs.Count(u => u.Pair.IsOnline);
    public int TotalPairs => _allPairs.Count;
    private bool _wasHovered = false;
    
    protected DrawFolderBase(IImmutableList<DrawUserPair> drawPairs, IImmutableList<Pair> allPairs, UiSharedService uiSharedService)
    {
        DrawPairs = drawPairs;
        _allPairs = allPairs;
        _uiSharedService = uiSharedService;
    }

    protected abstract bool RenderIfEmpty { get; }
    protected abstract bool RenderMenu { get; }
    protected abstract string ComponentId { get;  }

    public void Draw()
    {
        if (!RenderIfEmpty && !DrawPairs.Any()) return;

        using var id = ImRaii.PushId("folder_" + ComponentId);
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        using (ImRaii.Child("folder__" + ComponentId, new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            // draw opener
            var icon = IsOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

            ImGui.AlignTextToFramePadding();

            _uiSharedService.IconText(icon);
            if (ImGui.IsItemClicked())
            {
                ToggleOpen();
            }

            ImGui.SameLine();
            var leftSideEnd = DrawIcon();

            ImGui.SameLine();
            var rightSideStart = DrawRightSideInternal();

            // draw name
            ImGui.SameLine(leftSideEnd);
            DrawName(rightSideStart - leftSideEnd);
        }

        _wasHovered = ImGui.IsItemHovered();

        color.Dispose();

        ImGui.Separator();

        // if opened draw content
        if (IsOpen)
        {
            using var indent = ImRaii.PushIndent(_uiSharedService.GetIconSize(FontAwesomeIcon.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false);
            DrawOpenedContent();
        }
    }

    protected abstract float DrawIcon();

    protected abstract void DrawMenu(float menuWidth);

    protected abstract void DrawName(float width);

    protected abstract float DrawRightSide(float currentRightSideX);

    protected abstract bool IsOpen { get; }

    protected abstract void ToggleOpen();

    protected virtual void DrawOpenedContent()
    {
        if (DrawPairs.Any())
        {
            foreach (var item in DrawPairs)
            {
                item.DrawPairedClient();
            }
        }
        else
        {
            ImGui.TextUnformatted("No users (online)");
        }

        ImGui.Separator();
    }

    private float DrawRightSideInternal()
    {
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();

        // Flyout Menu
        var rightSideStart = windowEndX - (RenderMenu ? (barButtonSize.X + spacingX) : spacingX);

        if (RenderMenu)
        {
            ImGui.SameLine(windowEndX - barButtonSize.X);
            if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV, ComponentId))
            {
                ImGui.OpenPopup("User Flyout Menu");
            }
            if (ImGui.BeginPopup("User Flyout Menu"))
            {
                using (ImRaii.PushId($"buttons-{ComponentId}")) DrawMenu(_menuWidth);
                _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                ImGui.EndPopup();
            }
            else
            {
                _menuWidth = 0;
            }
        }

        return DrawRightSide(rightSideStart);
    }
}