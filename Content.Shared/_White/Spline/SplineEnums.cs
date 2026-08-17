// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._White.Spline.CatmullRom;
using Content.Shared._White.Spline.CubicBezier;
using Content.Shared._White.Spline.Linear;
using Vector4 = System.Numerics.Vector4;

namespace Content.Shared._White.Spline;

public static class Spline
{
    public static ISpline<Vector2> From2DType(Spline2DType type)
    {
        return type switch
        {
            Spline2DType.Linear     => new SplineLinear2D(),
            Spline2DType.CatmullRom => new SplineCatmullRom2D(),
            _                       => throw new NotImplementedException()
        };
    }

    public static ISpline<Vector4> From4DType(Spline4DType type)
    {
        return type switch
        {
            Spline4DType.Linear => new SplineLinear4D(),
            Spline4DType.Bezier => new SplineCubicBezier4D(),
            _                   => throw new NotImplementedException()
        };
    }

    public static ISpline<Color> FromColorType(SplineColorType type)
    {
        return type switch
        {
            SplineColorType.Linear => new SplineLinearColor(),
            SplineColorType.Bezier => new SplineCubicBezierColor(),
            _                      => throw new NotImplementedException()
        };
    }
}

public enum Spline2DType : byte
{
    Linear,
    CatmullRom
}

public enum Spline4DType : byte
{
    Linear,
    Bezier
}

public enum SplineColorType : byte
{
    Linear,
    Bezier
}

