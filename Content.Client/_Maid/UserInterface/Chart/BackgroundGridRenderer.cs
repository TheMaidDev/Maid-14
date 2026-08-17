using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Client._Maid.UserInterface.Chart;

/// <summary>
///     A background grid sub-renderer that draws vertical and horizontal grid lines.
/// </summary>
public sealed class BackgroundGridRenderer : IChartSubRenderer
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float GridSpacingX { get; set; } = 1.0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float GridSpacingY { get; set; } = 1.0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public Color GridColor { get; set; } = Color.FromHex("#333333");

    public BackgroundGridRenderer()
    {
    }

    public BackgroundGridRenderer(float gridSpacingX, float gridSpacingY, Color gridColor)
    {
        GridSpacingX = gridSpacingX;
        GridSpacingY = gridSpacingY;
        GridColor = gridColor;
    }

    public void Draw(DrawingHandleScreen handle, DrawContext context)
    {
        if (GridSpacingX <= 0f || GridSpacingY <= 0f)
            return;

        var viewport = context.GetViewport();
        var borders = context.GetBorders();

        var rangeX = viewport.Width;
        var rangeY = viewport.Height;

        var linesX = (int)MathF.Ceiling(rangeX / GridSpacingX);
        var linesY = (int)MathF.Ceiling(rangeY / GridSpacingY);

        // Safety limit to avoid freeze on extremely small spacing relative to viewport
        if (linesX > 500 || linesY > 500)
            return;

        var startX = MathF.Floor(viewport.Left / GridSpacingX) * GridSpacingX;
        var startY = MathF.Floor(viewport.Bottom / GridSpacingY) * GridSpacingY;

        // Draw vertical grid lines
        for (var x = startX; x <= viewport.Right; x += GridSpacingX)
        {
            var screenX = context.MapDataToScreen(new Vector2(x, 0)).X;
            handle.DrawLine(new Vector2(screenX, borders.Top), new Vector2(screenX, borders.Bottom), GridColor);
        }

        // Draw horizontal grid lines
        for (var y = startY; y <= viewport.Top; y += GridSpacingY)
        {
            var screenY = context.MapDataToScreen(new Vector2(0, y)).Y;
            handle.DrawLine(new Vector2(borders.Left, screenY), new Vector2(borders.Right, screenY), GridColor);
        }
    }
}
