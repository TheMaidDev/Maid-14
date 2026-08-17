using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Client._Maid.UserInterface.Chart;

/// <summary>
///     Control that manages and renders sub-renderers in local coordinate space.
/// </summary>
public sealed class ChartRenderer : Control
{
    private readonly List<IChartSubRenderer> _subRenderers = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public float ViewportMinX { get; set; } = 0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float ViewportMaxX { get; set; } = 10f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float ViewportMinY { get; set; } = 0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float ViewportMaxY { get; set; } = 10f;

    public IReadOnlyList<IChartSubRenderer> SubRenderers => _subRenderers;


    public Vector2? HoveredPointData { get; private set; }
    public Vector2? HoveredPointScreen { get; private set; }
    public float? HoveredX { get; private set; }
    public string? HoveredSeries { get; private set; }

    public event Action<float>? OnPointClicked;
    public event Action<float?>? OnPointHovered;

    private Vector2? _mousePosition;

    public ChartRenderer()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void AddSubRenderer(IChartSubRenderer subRenderer)
    {
        _subRenderers.Add(subRenderer);
    }

    public void RemoveSubRenderer(IChartSubRenderer subRenderer)
    {
        _subRenderers.Remove(subRenderer);
    }

    public void ClearSubRenderers()
    {
        _subRenderers.Clear();
    }

    public void SetViewport(float minX, float maxX, float minY, float maxY)
    {
        ViewportMinX = minX;
        ViewportMaxX = maxX;
        ViewportMinY = minY;
        ViewportMaxY = maxY;
        InvalidateMeasure();
    }

    /// <summary>
    ///     Adjusts the viewport to fit all bounded charts added to this renderer with the smallest bounding box.
    /// </summary>
    public void AutoFit()
    {
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        var hasData = false;

        foreach (var sub in _subRenderers)
        {
            if (sub is not IBoundedChartSubRenderer bounded)
                continue;

            var bounds = bounded.GetBounds();

            if (!bounds.HasValue)
                continue;

            minX = Math.Min(minX, bounds.Value.Left);
            maxX = Math.Max(maxX, bounds.Value.Right);
            minY = Math.Min(minY, bounds.Value.Bottom);
            maxY = Math.Max(maxY, bounds.Value.Top);
            hasData = true;
        }

        if (!hasData)
            return;

        // Pad collapsed ranges to avoid divide by zero / flatlines
        if (MathF.Abs(maxX - minX) < 1e-5f)
        {
            minX -= 1f;
            maxX += 1f;
        }

        if (MathF.Abs(maxY - minY) < 1e-5f)
        {
            minY -= 1f;
            maxY += 1f;
        }

        // Add 5% padding around the bounding box for a cleaner look
        var paddingX = (maxX - minX) * 0.05f;
        var paddingY = (maxY - minY) * 0.05f;

        SetViewport(minX - paddingX, maxX + paddingX, minY - paddingY, maxY + paddingY);
    }
    public Vector2 MapDataToScreen(Vector2 dataPoint)
    {
        var rangeX = ViewportMaxX - ViewportMinX;
        var rangeY = ViewportMaxY - ViewportMinY;

        if (MathF.Abs(rangeX) < 1e-6f)
            rangeX = 1f;
        if (MathF.Abs(rangeY) < 1e-6f)
            rangeY = 1f;
        var scaleX = PixelWidth / rangeX;
        var scaleY = -PixelHeight / rangeY;

        var posX = -ViewportMinX * scaleX;
        var posY = PixelHeight - ViewportMinY * scaleY;

        return new Vector2(posX + dataPoint.X * scaleX, posY + dataPoint.Y * scaleY);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mousePosition = args.RelativePosition;
        UpdateHoveredPoint();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _mousePosition = null;
        HoveredX = null;
        HoveredSeries = null;
        HoveredPointScreen = null;
        HoveredPointData = null;
        OnPointHovered?.Invoke(null);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            if (HoveredX != null)
            {
                OnPointClicked?.Invoke(HoveredX.Value);
                args.Handle();
            }
        }
    }


    private void UpdateHoveredPoint()
    {
        if (_mousePosition == null)
            return;

        float? newHoveredX = null;
        string? newHoveredSeries = null;
        Vector2? closestScreen = null;
        Vector2? closestData = null;
        var closestDistanceX = float.MaxValue;

        foreach (var sub in _subRenderers)
        {
            if (sub is ConnectedChartRenderer conn)
            {
                foreach (var p in conn.Points)
                {
                    var screenP = MapDataToScreen(p);
                    var distX = MathF.Abs(_mousePosition.Value.X - screenP.X);
                    if (distX < closestDistanceX)
                    {
                        closestDistanceX = distX;
                        newHoveredX = p.X;
                        newHoveredSeries = conn.Label;
                        closestScreen = screenP;
                        closestData = p;
                    }
                }
            }
        }

        if (closestDistanceX > 30f)
        {
            newHoveredX = null;
            newHoveredSeries = null;
            closestScreen = null;
            closestData = null;
        }

        if (newHoveredX != HoveredX || newHoveredSeries != HoveredSeries)
        {
            HoveredX = newHoveredX;
            HoveredSeries = newHoveredSeries;
            HoveredPointScreen = closestScreen;
            HoveredPointData = closestData;
            OnPointHovered?.Invoke(newHoveredX);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var rangeX = ViewportMaxX - ViewportMinX;
        var rangeY = ViewportMaxY - ViewportMinY;

        if (MathF.Abs(rangeX) < 1e-6f)
            rangeX = 1f;
        if (MathF.Abs(rangeY) < 1e-6f)
            rangeY = 1f;
        var scaleX = PixelWidth / rangeX;
        var scaleY = -PixelHeight / rangeY; // Negative Y scale to flip Y-axis (Y-up)

        var posX = -ViewportMinX * scaleX;
        var posY = PixelHeight - ViewportMinY * scaleY;

        var position = new Vector2(posX, posY);
        var scale = new Vector2(scaleX, scaleY);
        var borders = new UIBox2(0, 0, PixelWidth, PixelHeight);
        var viewport = new Box2(ViewportMinX, ViewportMinY, ViewportMaxX, ViewportMaxY);
        var context = new DrawContext(this, borders, viewport, position, scale);

        foreach (var sub in _subRenderers)
        {
            sub.Draw(handle, context);
        }

        if (HoveredPointScreen != null)
        {
            handle.DrawCircle(HoveredPointScreen.Value, 5f, Color.Gold);
            handle.DrawCircle(HoveredPointScreen.Value, 7f, Color.Gold.WithAlpha(0.5f), false);
        }
    }

}
