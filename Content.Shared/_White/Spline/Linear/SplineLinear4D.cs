// SPDX-License-Identifier: AGPL-3.0-or-later

using Vector4 = System.Numerics.Vector4;
using System.Runtime.CompilerServices;

namespace Content.Shared._White.Spline.Linear;

public sealed class SplineLinear4D : SplineLinear<Vector4>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Vector4 Add(Vector4 op1, Vector4 op2)
    {
        return new Vector4(op1.X + op2.X, op1.Y + op2.Y, op1.Z + op2.Z, op1.W + op2.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Vector4 Subtract(Vector4 op1, Vector4 op2)
    {
        return new Vector4(op1.X - op2.X, op1.Y - op2.Y, op1.Z - op2.Z, op1.W - op2.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override float Magnitude(Vector4 op1)
    {
        return MathF.Sqrt(op1.X * op1.X + op1.Y * op1.Y + op1.Z * op1.Z + op1.W * op1.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Vector4 Multiply(Vector4 op1, float scalar)
    {
        return new Vector4(op1.X * scalar, op1.Y * scalar, op1.Z * scalar, op1.W * scalar);
    }
}
