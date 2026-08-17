using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Client._Maid.UserInterface.Chart;

/// <summary>
///     Renders tick labels (numbers) equally spaced along the bottom (X) and left (Y) sides of the chart.
/// </summary>
public sealed class ChartAxesRenderer : IChartSubRenderer
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float SpacingX { get; set; } = 1.0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float SpacingY { get; set; } = 1.0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public Color TextColor { get; set; } = Color.White;

    [ViewVariables(VVAccess.ReadWrite)]
    public int FontSize { get; set; } = 9;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool DrawXLabels { get; set; } = true;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool DrawYLabels { get; set; } = true;

    public ChartAxesRenderer()
    {
    }

    public ChartAxesRenderer(float spacingX, float spacingY, Color textColor, int fontSize = 9)
    {
        SpacingX = spacingX;
        SpacingY = spacingY;
        TextColor = textColor;
        FontSize = fontSize;
    }

    public void Draw(DrawingHandleScreen handle, DrawContext context)
    {
        if (SpacingX <= 0f || SpacingY <= 0f)
            return;

        var cache = IoCManager.Resolve<IResourceCache>();
        var font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize);

        var viewport = context.GetViewport();
        var borders = context.GetBorders();

        var rangeX = viewport.Width;
        var rangeY = viewport.Height;

        var ticksX = (int)MathF.Ceiling(rangeX / SpacingX);
        var ticksY = (int)MathF.Ceiling(rangeY / SpacingY);

        // Safety limit to avoid freeze on extremely small spacing relative to viewport
        if (ticksX > 500 || ticksY > 500)
            return;

        var startX = MathF.Floor(viewport.Left / SpacingX) * SpacingX;
        var startY = MathF.Floor(viewport.Bottom / SpacingY) * SpacingY;

        // Draw X Axis Numbers
        if (DrawXLabels)
        {
            for (var x = startX; x <= viewport.Right; x += SpacingX)
            {
                var screenX = context.MapDataToScreen(new Vector2(x, 0)).X;

                // Avoid drawing outside horizontal borders
                if (screenX < borders.Left || screenX > borders.Right)
                    continue;

                var text = x.ToString("0.##");
                var size = handle.GetDimensions(font, text, 1f);

                // Draw at the bottom of the chart
                var pos = new Vector2(screenX - size.X / 2f, borders.Bottom - size.Y - 4f);
                handle.DrawString(font, pos, text, TextColor);
            }
        }

        // Draw Y Axis Numbers
        if (DrawYLabels)
        {
            for (var y = startY; y <= viewport.Top; y += SpacingY)
            {
                var screenY = context.MapDataToScreen(new Vector2(0, y)).Y;

                // Avoid drawing outside vertical borders
                if (screenY < borders.Top || screenY > borders.Bottom)
                    continue;

                var text = y.ToString("0.##");
                var size = handle.GetDimensions(font, text, 1f);

                // Draw on the left side of the chart
                var pos = new Vector2(borders.Left + 4f, screenY - size.Y / 2f);
                handle.DrawString(font, pos, text, TextColor);
            }
        }
    }
}
