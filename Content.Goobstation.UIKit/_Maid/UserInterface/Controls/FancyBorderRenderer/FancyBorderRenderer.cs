using Robust.Client.Graphics;
using System.Numerics;

namespace Content.Goobstation.UIKit._Maid.UserInterface.Controls;

internal static class FancyBorderRenderer
{
    private const int CornerSegments = 6;
    private const float OuterRadius = 4f;
    private const float NotchRadius = 4f;
    private const float NotchOffsetX = 22f;
    private const float NotchPaddingX = 8f;
    private const float NotchPaddingY = 2.5f;
    private const int Margin = 12;

    public static void DrawFancyBorder(
        DrawingHandleScreen handle,
        UIBox2 bounds,
        float firstLineWidth,
        float firstLineHeight)
    {
        var fill = Color.FromHex("#4d4d4d");
        var stroke = Color.FromHex("#8b8b8c");

        var frameTopOffset = firstLineHeight;
        var adjustedBoundsTop = bounds.Top + frameTopOffset;
        var notchLeft = bounds.Left + NotchOffsetX;
        var notchWidth = firstLineWidth + (NotchPaddingX * 2);
        var notchRight = notchLeft + notchWidth;
        var notchTop = adjustedBoundsTop;
        var notchBottom = adjustedBoundsTop + (firstLineHeight / 2) + NotchPaddingY;

        // MAID BEGIN rt-update UIBox2 validates its input since RT 280/286, never build an inverted box
        var adjustedLeft = bounds.Left + Margin;
        var adjustedRight = Math.Max(adjustedLeft, bounds.Right - Margin);
        var adjustedBounds = new UIBox2(adjustedLeft, adjustedBoundsTop, adjustedRight, Math.Max(adjustedBoundsTop, bounds.Bottom));
        // MAID END rt-update

        var fillTriangles = BuildFill(adjustedBounds, notchLeft, notchRight, notchBottom, OuterRadius);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, fillTriangles, fill);

        var strokeVertices = BuildStroke(adjustedBounds, notchLeft, notchRight, notchTop, notchBottom, OuterRadius, NotchRadius);
        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, strokeVertices, stroke);
    }

    private static List<Vector2> BuildFill(
        UIBox2 bounds,
        float notchLeft,
        float notchRight,
        float notchBottom,
        float outerRadius)
    {
        var triangles = new List<Vector2>();
        var left = bounds.Left;
        var right = bounds.Right;
        var top = bounds.Top;
        var bottom = bounds.Bottom;

        // Bottom rectangle
        AddQuad(triangles, new Vector2(left, notchBottom), new Vector2(right, notchBottom), new Vector2(right, bottom), new Vector2(left, bottom));
        // Left top rectangle
        AddQuad(triangles, new Vector2(left, top), new Vector2(notchLeft, top), new Vector2(notchLeft, notchBottom), new Vector2(left, notchBottom));
        // Right top rectangle
        AddQuad(triangles, new Vector2(notchRight, top), new Vector2(right, top), new Vector2(right, notchBottom), new Vector2(notchRight, notchBottom));

        // Outer corners: top-left, top-right, bottom-right, bottom-left
        AddArcFill(triangles, left + outerRadius, top + outerRadius, outerRadius, 180f, 270f);
        AddArcFill(triangles, right - outerRadius, top + outerRadius, outerRadius, 270f, 360f);
        AddArcFill(triangles, right - outerRadius, bottom - outerRadius, outerRadius, 0f, 90f);
        AddArcFill(triangles, left + outerRadius, bottom - outerRadius, outerRadius, 90f, 180f);

        return triangles;
    }

    private static void AddQuad(List<Vector2> triangles, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
    }

    private static void AddArcFill(List<Vector2> triangles, float centerX, float centerY, float radius, float startAngle, float endAngle)
    {
        var step = (endAngle - startAngle) / CornerSegments;
        for (var i = 0; i < CornerSegments; i++)
        {
            var a1 = (startAngle + i * step) * MathF.PI / 180f;
            var a2 = (startAngle + (i + 1) * step) * MathF.PI / 180f;
            var p1 = new Vector2(centerX + radius * MathF.Cos(a1), centerY + radius * MathF.Sin(a1));
            var p2 = new Vector2(centerX + radius * MathF.Cos(a2), centerY + radius * MathF.Sin(a2));
            var center = new Vector2(centerX, centerY);
            triangles.Add(center); triangles.Add(p1); triangles.Add(p2);
        }
    }

    private static List<Vector2> BuildStroke(
        UIBox2 bounds,
        float notchLeft,
        float notchRight,
        float notchTop,
        float notchBottom,
        float outerRadius,
        float notchRadius)
    {
        var vertices = new List<Vector2>();
        var left = bounds.Left;
        var right = bounds.Right;
        var top = bounds.Top;
        var bottom = bounds.Bottom;

        void AddLine(Vector2 a, Vector2 b) { vertices.Add(a); vertices.Add(b); }
        void AddPolyLine(List<Vector2> points) { for (var i = 0; i < points.Count - 1; i++) AddLine(points[i], points[i + 1]); }

        // Top-left: (left, top+radius) -> arc -> (notchLeft, top)
        var topLeft = new List<Vector2>();
        topLeft.Add(new Vector2(left, top + outerRadius));
        AddArc(topLeft, left + outerRadius, top + outerRadius, outerRadius, 180f, 270f);
        topLeft.Add(new Vector2(left + outerRadius, top));
        topLeft.Add(new Vector2(notchLeft, top));
        AddPolyLine(topLeft);

        // Top-right: (notchRight, top) -> (right-radius, top) -> arc -> (right, top+radius)
        var topRight = new List<Vector2>();
        topRight.Add(new Vector2(notchRight, top));
        topRight.Add(new Vector2(right - outerRadius, top));
        AddArc(topRight, right - outerRadius, top + outerRadius, outerRadius, 270f, 360f);
        topRight.Add(new Vector2(right, top + outerRadius));
        AddPolyLine(topRight);

        // Sides+bottom: left -> down -> arc -> bottom line -> arc -> right -> up
        var sidesBottom = new List<Vector2>();
        sidesBottom.Add(new Vector2(left, top + outerRadius));
        sidesBottom.Add(new Vector2(left, bottom - outerRadius));
        AddArc(sidesBottom, left + outerRadius, bottom - outerRadius, outerRadius, 180f, 90f);
        sidesBottom.Add(new Vector2(left + outerRadius, bottom));
        sidesBottom.Add(new Vector2(right - outerRadius, bottom));
        AddArc(sidesBottom, right - outerRadius, bottom - outerRadius, outerRadius, 90f, 0f);
        sidesBottom.Add(new Vector2(right, bottom - outerRadius));
        sidesBottom.Add(new Vector2(right, top + outerRadius));
        AddPolyLine(sidesBottom);

        // Notch: right side -> arc -> bottom arc -> left side
        var notch = new List<Vector2>();
        notch.Add(new Vector2(notchRight, notchTop));
        notch.Add(new Vector2(notchRight, notchBottom - notchRadius));
        AddArc(notch, notchRight - notchRadius, notchBottom - notchRadius, notchRadius, 0f, 90f);
        notch.Add(new Vector2(notchLeft + notchRadius, notchBottom));
        AddArc(notch, notchLeft + notchRadius, notchBottom - notchRadius, notchRadius, 90f, 180f);
        notch.Add(new Vector2(notchLeft, notchBottom - notchRadius));
        notch.Add(new Vector2(notchLeft, notchTop));
        AddPolyLine(notch);

        return vertices;
    }

    private static void AddArc(List<Vector2> vertices, float centerX, float centerY, float radius, float startAngle, float endAngle)
    {
        for (var i = 1; i <= CornerSegments; i++)
        {
            var angle = (startAngle + (endAngle - startAngle) * i / CornerSegments) * MathF.PI / 180f;
            vertices.Add(new Vector2(centerX + radius * MathF.Cos(angle), centerY + radius * MathF.Sin(angle)));
        }
    }
}
