using Content.Shared._Maid.CVars;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    [Dependency] private readonly IConfigurationManager _maidCfg = default!;

    private bool _maidClassicLayout;
    private bool _maidBottomVisible = true;

    private void MaidInitializeLayout()
    {
        SwitchLayoutButton.OnPressed += _ =>
            _maidCfg.SetCVar(MaidCVars.LobbyClassicLayout, !_maidCfg.GetCVar(MaidCVars.LobbyClassicLayout));
        _maidCfg.OnValueChanged(MaidCVars.LobbyClassicLayout, MaidApplyLayout, true);
    }

    private void MaidSetBottomVisible(bool visible)
    {
        _maidBottomVisible = visible;
        MaidUpdateVisibility();
    }

    private void MaidApplyLayout(bool classic)
    {
        _maidClassicLayout = classic;

        if (classic)
        {
            MaidMove(CharacterPanel, ClassicCharacterSlot);
            MaidMove(ServerName, ClassicInfoBox);
            MaidMove(ButtonsStripe, ClassicInfoBox);
            MaidMove(ServerInfo, ClassicInfoBox);
            MaidMove(StationTime, ClassicInfoBox);
            MaidMove(ChatPanel, ClassicChatSlot);
            CharacterPanel.SetHeight = 390;
            PreferencesWidget.Visible = true;
        }
        else
        {
            MaidMove(CharacterPanel, WdCharacterSlot);
            MaidMove(ServerName, ChatPanelBox);
            ServerName.SetPositionInParent(0);
            MaidMove(ButtonsStripe, ChatPanelBox);
            ButtonsStripe.SetPositionInParent(1);
            MaidMove(ServerInfo, WdInfoBox);
            MaidMove(StationTime, WdInfoBox);
            MaidMove(ChatPanel, WdChatSlot);
            CharacterPanel.SetHeight = float.NaN;
            PreferencesWidget.Visible = false;
        }

        MaidUpdateVisibility();
    }

    private void MaidUpdateVisibility()
    {
        WdLayout.Visible = _maidBottomVisible && !_maidClassicLayout;
        ClassicLayout.Visible = _maidBottomVisible && _maidClassicLayout;
    }

    private static void MaidMove(Control child, Control newParent)
    {
        if (child.Parent == newParent)
            return;

        child.Orphan();
        newParent.AddChild(child);
    }
}
