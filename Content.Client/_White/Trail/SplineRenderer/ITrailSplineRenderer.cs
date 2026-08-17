// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._White.Spline;
using Content.Shared._White.Trail;
using Robust.Client.Graphics;
using Vector4 = System.Numerics.Vector4;

namespace Content.Client._White.Trail.SplineRenderer;

public interface ITrailSplineRenderer
{
    void Render(
        DrawingHandleWorld handle,
        Texture? texture,
        ISpline<Vector2> splineIterator,
        ISpline<Vector4> gradientIterator,
        ITrailSettings settings,
        Vector2[] paPositions,
        float[] paLifetimes
    );
}
