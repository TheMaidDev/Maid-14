// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Maid.Lobby.UI;

/// <summary>
/// Section title with a gold underline that hooks up on the right.
/// </summary>
public sealed class LobbyBracketHeading : Control
{
    private static readonly Color TitleColor = StyleNano.LobbyLavender;
    private static readonly Color BracketColor = StyleNano.LobbyGold;

    private readonly Label _label;

    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public LobbyBracketHeading()
    {
        _label = new Label
        {
            StyleClasses = { StyleNano.StyleClassLobbyHeading },
            FontColorOverride = TitleColor,
            HorizontalAlignment = HAlignment.Left,
        };
        VerticalExpand = false;
        AddChild(_label);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(availableSize);
        return _label.DesiredSize + new Vector2(10, 4);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _label.Arrange(UIBox2.FromDimensions(Vector2.Zero, _label.DesiredSize));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var textWidth = _label.DesiredPixelSize.X;
        var baseline = _label.DesiredPixelSize.Y + scale;
        var end = textWidth + 8f * scale;
        var hook = 8f * scale;

        handle.DrawLine(new Vector2(0, baseline), new Vector2(end, baseline), BracketColor);
        handle.DrawLine(new Vector2(0, baseline + scale), new Vector2(end, baseline + scale), BracketColor);
        handle.DrawLine(new Vector2(end, baseline + scale), new Vector2(end, baseline - hook), BracketColor);
        handle.DrawLine(new Vector2(end + scale, baseline + scale), new Vector2(end + scale, baseline - hook), BracketColor);
    }
}
