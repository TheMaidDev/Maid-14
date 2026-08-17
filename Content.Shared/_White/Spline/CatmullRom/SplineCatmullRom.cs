// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Runtime.CompilerServices;

namespace Content.Shared._White.Spline.CatmullRom;

public abstract class SplineCatmullRom<T> : Spline<T>
{
    protected const int LookupPrecision = 100;

    protected static readonly (float c0, float c1, float c2, float c3)[] PositionCoefficientLookup
        = Enumerable.Range(0, LookupPrecision + 1)
            .Select(x => CalculateCoefficientsPosition((float) x / LookupPrecision)).ToArray();

    protected static readonly (float c0, float c1, float c2, float c3)[] GradientCoefficientLookup
        = Enumerable.Range(0, LookupPrecision + 1)
            .Select(x => CalculateCoefficientsTangent((float) x / LookupPrecision)).ToArray();

    protected static (float c0, float c1, float c2, float c3) CalculateCoefficientsPosition(float t)
    {
        var tt = t * t;
        var ttt = tt * t;
        return (
            -ttt + 2.0f * tt - t,
            3.0f * ttt - 5.0f * tt + 2.0f,
            -3.0f * ttt + 4.0f * tt + t,
            ttt - tt
        );
    }

    protected static (float c0, float c1, float c2, float c3) CalculateCoefficientsTangent(float t)
    {
        var tt = t * t;
        return (
            -3.0f * tt + 4.0f * t - 1,
            9.0f * tt - 10.0f * t,
            -9.0f * tt + 8.0f * t + 1.0f,
            3.0f * tt - 2.0f * t
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetLookupIndex(float t)
    {
        return (int) (t * LookupPrecision);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T SamplePosition(ReadOnlySpan<T> controlPoints, float u)
    {
        return CalculateCatmullRom(GetCurrentControlPoints(controlPoints, (int) u),
            PositionCoefficientLookup[GetLookupIndex(u % 1)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T SampleVelocity(ReadOnlySpan<T> controlPoints, float u)
    {
        return CalculateCatmullRom(GetCurrentControlPoints(controlPoints, (int) u),
            GradientCoefficientLookup[GetLookupIndex(u % 1)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override (T Position, T Velocity) SamplePositionVelocity(ReadOnlySpan<T> controlPoints, float u)
    {
        var lookupIndex = GetLookupIndex(u % 1);
        var currentControlPoints = GetCurrentControlPoints(controlPoints, (int) u);
        return (
            CalculateCatmullRom(currentControlPoints, PositionCoefficientLookup[lookupIndex]),
            CalculateCatmullRom(currentControlPoints, GradientCoefficientLookup[lookupIndex])
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual (T p0, T p1, T p2, T p3) GetCurrentControlPoints(ReadOnlySpan<T> controlPoints, int u)
    {
        var p1 = controlPoints[u];
        var p2 = controlPoints[u + 1];
        var p0 = u == 0 ? Add(p1, Subtract(p1, p2)) : controlPoints[u - 1];
        var p3 = u + 2 == controlPoints.Length ? Add(p2, Subtract(p2, p2)) : controlPoints[u + 2];
        return (p0, p1, p2, p3);
    }

    protected abstract T CalculateCatmullRom(
        (T p0, T p1, T p2, T p3) points,
        (float c0, float c1, float c2, float c3) coeffs);
}
