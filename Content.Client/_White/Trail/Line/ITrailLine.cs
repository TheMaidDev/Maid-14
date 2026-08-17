// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._White.Trail;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client._White.Trail.Line;

public interface ITrailLine
{
    ITrailSettings Settings { get; }

    void TryCreateSegment((Vector2 WorldPosition, Angle WorldRotation) worldPosRot, MapId mapId);

    void Render(DrawingHandleWorld handle, Texture? texture);
}
