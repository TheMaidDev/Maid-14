// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Maid.Lobby.UI;

/// <summary>
/// Tic-tac-toe style ready marker: hollow circle when ready, X when not.
/// </summary>
public sealed class ReadyStatusIcon : Control
{
    public enum ReadyIconMode : byte
    {
        NotReady,
        Ready,
        Join
    }

    private ReadyIconMode _mode = ReadyIconMode.NotReady;
    private Color _color = Color.FromHex("#D16E6E");

    public ReadyIconMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            _mode = value;
            InvalidateMeasure();
        }
    }

        public Color IconColor
        {
            get => _color;
            set => _color = value;
        }

    public ReadyStatusIcon()
    {
        MinSize = new Vector2(18, 18);
        SetSize = new Vector2(18, 18);
        MouseFilter = MouseFilterMode.Ignore;
        VerticalAlignment = VAlignment.Center;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return MinSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var size = PixelSize;
        var center = size / 2f;
        var radius = MathF.Min(size.X, size.Y) * 0.38f;
        var color = _color;

        switch (_mode)
        {
            case ReadyIconMode.Ready:
                // Hollow circle — like a recording LED / computer button.
                handle.DrawCircle(center, radius, color, filled: false);
                handle.DrawCircle(center, radius - MathF.Max(1.2f * scale, 1f), color, filled: false);
                break;

            case ReadyIconMode.NotReady:
            {
                var inset = radius * 0.55f;
                var thickness = MathF.Max(1.6f * scale, 1.5f);
                DrawThickLine(handle,
                    center + new Vector2(-inset, -inset),
                    center + new Vector2(inset, inset),
                    color,
                    thickness);
                DrawThickLine(handle,
                    center + new Vector2(inset, -inset),
                    center + new Vector2(-inset, inset),
                    color,
                    thickness);
                break;
            }

            case ReadyIconMode.Join:
            {
                // Simple chevron / arrow into the round.
                var inset = radius * 0.7f;
                var thickness = MathF.Max(1.6f * scale, 1.5f);
                DrawThickLine(handle,
                    center + new Vector2(-inset * 0.4f, -inset),
                    center + new Vector2(inset * 0.6f, 0f),
                    color,
                    thickness);
                DrawThickLine(handle,
                    center + new Vector2(inset * 0.6f, 0f),
                    center + new Vector2(-inset * 0.4f, inset),
                    color,
                    thickness);
                break;
            }
        }
    }

    private static void DrawThickLine(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color, float thickness)
    {
        var dir = to - from;
        if (dir.LengthSquared() < 0.01f)
            return;

        var normal = Vector2.Normalize(new Vector2(-dir.Y, dir.X));
        var steps = Math.Max(1, (int) MathF.Ceiling(thickness));
        var half = (steps - 1) * 0.5f;
        for (var i = 0; i < steps; i++)
        {
            var offset = normal * (i - half);
            handle.DrawLine(from + offset, to + offset, color);
        }
    }
}
