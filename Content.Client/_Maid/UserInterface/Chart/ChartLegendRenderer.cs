using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Client._Maid.UserInterface.Chart;

public enum LegendPosition
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft
}

/// <summary>
///     A sub-renderer that displays a legend identifying the labels and colors of connected charts.
/// </summary>
public sealed class ChartLegendRenderer : IChartSubRenderer
{
    [ViewVariables(VVAccess.ReadWrite)]
    public LegendPosition Position { get; set; } = LegendPosition.TopRight;

    [ViewVariables(VVAccess.ReadWrite)]
    public int FontSize { get; set; } = 10;

    [ViewVariables(VVAccess.ReadWrite)]
    public Color TextColor { get; set; } = Color.White;

    [ViewVariables(VVAccess.ReadWrite)]
    public Color BackgroundColor { get; set; } = Color.Black.WithAlpha(0.6f);

    public ChartLegendRenderer()
    {
    }

    public ChartLegendRenderer(LegendPosition position, int fontSize = 10)
    {
        Position = position;
        FontSize = fontSize;
    }

    public void Draw(DrawingHandleScreen handle, DrawContext context)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        var font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize);

        var subRenderers = context.GetSubRenderers();
        var items = new List<(string Label, Color Color)>();

        foreach (var sub in subRenderers)
        {
            if (sub is ConnectedChartRenderer chart && !string.IsNullOrEmpty(chart.Label))
            {
                items.Add((chart.Label, chart.Color));
            }
        }

        if (items.Count == 0)
            return;

        var borders = context.GetBorders();

        // Calculate layout size of the legend
        var itemHeight = font.GetLineHeight(1f) + 4f;
        var totalHeight = items.Count * itemHeight;
        var maxWidth = 0f;

        foreach (var item in items)
        {
            var size = handle.GetDimensions(font, item.Label, 1f);
            if (size.X > maxWidth)
                maxWidth = size.X;
        }

        var boxWidth = maxWidth + 32f; // Width for label + color line + padding
        var boxHeight = totalHeight + 8f; // Height with top/bottom padding

        // Determine top-left corner of the legend box based on configuration
        float startX;
        float startY;

        switch (Position)
        {
            case LegendPosition.TopLeft:
                startX = borders.Left + 10f;
                startY = borders.Top + 10f;
                break;
            case LegendPosition.BottomLeft:
                startX = borders.Left + 10f;
                startY = borders.Bottom - boxHeight - 10f;
                break;
            case LegendPosition.BottomRight:
                startX = borders.Right - boxWidth - 10f;
                startY = borders.Bottom - boxHeight - 10f;
                break;
            case LegendPosition.TopRight:
            default:
                startX = borders.Right - boxWidth - 10f;
                startY = borders.Top + 10f;
                break;
        }

        // Draw background box for legend
        var legendBox = new UIBox2(startX, startY, startX + boxWidth, startY + boxHeight);
        handle.DrawRect(legendBox, BackgroundColor);

        // Draw items equally spaced
        var currentY = startY + 6f;
        foreach (var item in items)
        {
            // Draw color line indicator
            var lineStartX = startX + 8f;
            var lineEndX = startX + 22f;
            var lineY = currentY + itemHeight / 2f - 1f;
            handle.DrawLine(new Vector2(lineStartX, lineY), new Vector2(lineEndX, lineY), item.Color);

            // Draw label text
            var textPos = new Vector2(startX + 26f, currentY);
            handle.DrawString(font, textPos, item.Label, TextColor);

            currentY += itemHeight;
        }
    }
}
