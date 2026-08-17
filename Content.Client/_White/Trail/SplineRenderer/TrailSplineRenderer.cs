// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._White.Trail;

namespace Content.Client._White.Trail.SplineRenderer;

public static class TrailSplineRenderer
{
    public static ITrailSplineRenderer FromType(TrailSplineRendererType type)
    {
        return type switch
        {
            TrailSplineRendererType.Continuous => new TrailSplineRendererContinuous(),
            TrailSplineRendererType.Point      => new TrailSplineRendererPoint(),
            TrailSplineRendererType.Debug      => new TrailSplineRendererDebug(),
            _                                  => throw new NotImplementedException()
        };
    }
}
