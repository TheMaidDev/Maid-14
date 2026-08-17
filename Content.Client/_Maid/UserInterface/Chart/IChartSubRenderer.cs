using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._Maid.UserInterface.Chart;

/// <summary>
///     Provides context for drawing within the ChartRenderer, including boundaries and coordinate mapping.
/// </summary>
public sealed class DrawContext
{
    private readonly ChartRenderer _parent;
    private readonly UIBox2 _borders;
    private readonly Box2 _viewport;
    private readonly Vector2 _position;
    private readonly Vector2 _scale;

    public DrawContext(ChartRenderer parent, UIBox2 borders, Box2 viewport, Vector2 position, Vector2 scale)
    {
        _parent = parent;
        _borders = borders;
        _viewport = viewport;
        _position = position;
        _scale = scale;
    }

    public UIBox2 GetBorders() => _borders;

    public Box2 GetViewport() => _viewport;

    public Vector2 MapDataToScreen(Vector2 dataPoint)
    {
        return _position + dataPoint * _scale;
    }

    public void DrawAtPos(DrawingHandleScreen handle, Vector2 dataPoint, Action drawAction)
    {
        var screenPos = MapDataToScreen(dataPoint);
        var oldTransform = handle.GetTransform();
        var matrix = oldTransform * Matrix3Helpers.CreateTransform(screenPos, Angle.Zero, Vector2.One);
        handle.SetTransform(matrix);
        drawAction();
        handle.SetTransform(oldTransform);
    }

    public IReadOnlyList<IChartSubRenderer> GetSubRenderers()
    {
        return _parent.SubRenderers;
    }
}

/// <summary>
///     Interface for sub-renderers that draw inside the ChartRenderer.
/// </summary>
public interface IChartSubRenderer
{
    void Draw(DrawingHandleScreen handle, DrawContext context);
}

/// <summary>
///     Interface for sub-renderers that have data boundaries (e.g. data charts).
/// </summary>
public interface IBoundedChartSubRenderer : IChartSubRenderer
{
    Box2? GetBounds();
}
