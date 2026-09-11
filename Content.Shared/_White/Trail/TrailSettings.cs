// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._White.Spline;
using Robust.Shared.Serialization;
using Vector4 = System.Numerics.Vector4;

namespace Content.Shared._White.Trail;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class TrailSettings : ITrailSettings
{
    public static readonly TrailSettings Default = new();

    public bool Enabled { get; set; } = true;

    public Vector2 Scale { get; set; } = new(0.5f, 1f);

    public float СreationDistanceThresholdSquared { get; set; } = 0.1f;

    public SegmentCreationMethod СreationMethod { get; set; } = SegmentCreationMethod.OnFrameUpdate;

    public Vector2 CreationOffset { get; set; } = Vector2.Zero;

    public Vector2 Gravity { get; set; } = new(0.01f, 0.01f);

    public Vector2 MaxRandomWalk { get; set; } = new(0.005f, 0.005f);

    public float Lifetime { get; set; }

    public float LengthStep { get; set; } = 0.1f;

    public string? TexurePath { get; set; }

    public Vector4[] Gradient { get; set; } = { new(1f, 1f, 1f, 1f), new(1f, 1f, 1f, 0f) };

    public Spline4DType GradientIteratorType { get; set; }

    public Spline2DType SplineIteratorType { get; set; }

    public TrailSplineRendererType SplineRendererType { get; set; }

    public bool OptionsConcealable { get; set; }

    public static void Inject(ITrailSettings into, ITrailSettings from)
    {
        into.Enabled = from.Enabled;
        into.Scale = from.Scale;
        into.СreationDistanceThresholdSquared = from.СreationDistanceThresholdSquared;
        into.СreationMethod = from.СreationMethod;
        into.CreationOffset = from.CreationOffset;
        into.Gravity = from.Gravity;
        into.MaxRandomWalk = from.MaxRandomWalk;
        into.Lifetime = from.Lifetime;
        into.LengthStep = from.LengthStep;
        into.TexurePath = from.TexurePath;
        into.Gradient = from.Gradient;
        into.SplineIteratorType = from.SplineIteratorType;
        into.SplineRendererType = from.SplineRendererType;
        into.OptionsConcealable = from.OptionsConcealable;
    }
}

public interface ITrailSettings
{
    bool Enabled { get; set; }

    Vector2 Gravity { get; set; }

    float Lifetime { get; set; }

    float LengthStep { get; set; }

    Vector2 MaxRandomWalk { get; set; }

    Vector2 Scale { get; set; }

    string? TexurePath { get; set; }

    Vector2 CreationOffset { get; set; }

    float СreationDistanceThresholdSquared { get; set; }

    SegmentCreationMethod СreationMethod { get; set; }

    Vector4[] Gradient { get; set; }

    Spline4DType GradientIteratorType { get; set; }

    Spline2DType SplineIteratorType { get; set; }

    TrailSplineRendererType SplineRendererType { get; set; }

    bool OptionsConcealable { get; set; }
}

public enum SegmentCreationMethod : byte
{
    OnFrameUpdate,
    OnMove
}

public enum TrailSplineRendererType : byte
{
    Continuous,
    Point,
    Debug
}
