using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Client._Maid.UserInterface.Chart;

/// <summary>
///     A chart sub-renderer that renders a list of connected points (Vec2).
/// </summary>
public sealed class ConnectedChartRenderer : IBoundedChartSubRenderer
{
    [ViewVariables(VVAccess.ReadWrite)]
    public List<Vector2> Points { get; set; } = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public Color Color { get; set; } = Color.White;

    [ViewVariables(VVAccess.ReadWrite)]
    public string Label { get; set; } = string.Empty;

    public ConnectedChartRenderer()
    {
    }

    public ConnectedChartRenderer(List<Vector2> points, Color color, string label = "")
    {
        Points = points;
        Color = color;
        Label = label;
    }

    public void Draw(DrawingHandleScreen handle, DrawContext context)
    {
        if (Points.Count < 2)
            return;

        for (var i = 0; i < Points.Count - 1; i++)
        {
            var p1 = Points[i];
            var p2 = Points[i + 1];

            var screenP1 = context.MapDataToScreen(p1);
            var screenP2 = context.MapDataToScreen(p2);

            handle.DrawLine(screenP1, screenP2, Color);
        }
    }

    public Box2? GetBounds()
    {
        if (Points.Count == 0)
            return null;

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;

        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        return new Box2(minX, minY, maxX, maxY);
    }
}
