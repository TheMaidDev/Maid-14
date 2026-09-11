// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._White.Spline;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Vector4 = System.Numerics.Vector4;

namespace Content.Shared._White.Trail;

[NetworkedComponent]
public abstract partial class SharedBulletTrailComponent : Component, ITrailSettings
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual bool Enabled { get; set; } = true;

    [DataField("gravity")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Vector2 Gravity { get; set; }

    [DataField("lifetime", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual float Lifetime { get; set; }

    [DataField("lengthStep")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual float LengthStep { get; set; }

    [DataField("randomWalk")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Vector2 MaxRandomWalk { get; set; }

    [DataField("scale", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Vector2 Scale { get; set; }

    [DataField("texturePath")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual string? TexurePath { get; set; }

    [DataField("creationOffset")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Vector2 CreationOffset { get; set; }

    [DataField("сreationDistanceThresholdSquared")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual float СreationDistanceThresholdSquared { get; set; }

    [DataField("creationMethod")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual SegmentCreationMethod СreationMethod { get; set; }

    [DataField("gradient", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Vector4[] Gradient { get; set; } = new[] { Vector4.One, new Vector4(1f, 1f, 1f, 0f) };

    [DataField("gradientIteratorType")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Spline4DType GradientIteratorType { get; set; }

    [DataField("splineIteratorType")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual Spline2DType SplineIteratorType { get; set; }

    [DataField("splineRendererType")]
    [ViewVariables(VVAccess.ReadWrite)]
    public virtual TrailSplineRendererType SplineRendererType { get; set; }

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public virtual bool OptionsConcealable { get; set; }
}

[Serializable, NetSerializable]
public sealed class BulletTrailComponentState : ComponentState
{
    public TrailSettings Settings;

    public BulletTrailComponentState(TrailSettings settings)
    {
        Settings = settings;
    }
}
