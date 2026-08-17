using System.Globalization;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters;

[DataDefinition]
public sealed partial class ScoreSlope
{
    [DataField]
    public float Base = 0f;

    [DataField]
    public float? Target = null;

    [DataField]
    public TimeSpan In = TimeSpan.FromMinutes(15);

    public float GetScore(TimeSpan time)
    {
        if (Target is null)
            return Base;

        if (time <= TimeSpan.Zero)
            return Base;

        if (time >= In)
            return Target.Value;

        return Base + (Target.Value - Base) * (float)(time.TotalSeconds / In.TotalSeconds);
    }
}

[TypeSerializer]
public sealed class ScoreSlopeSerializer : ITypeSerializer<ScoreSlope, ValueDataNode>
{
    public ScoreSlope Read(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<ScoreSlope>? instanceProvider = null)
    {
        if (!float.TryParse(node.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var baseVal))
            throw new FormatException($"Failed to parse float for ScoreSlope: {node.Value}");

        return new ScoreSlope { Base = baseVal };
    }

    public ValidationNode Validate(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        return float.TryParse(node.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, "Failed parsing float for ScoreSlope");
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        ScoreSlope value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        return new ValueDataNode(value.Base.ToString(CultureInfo.InvariantCulture));
    }
}
