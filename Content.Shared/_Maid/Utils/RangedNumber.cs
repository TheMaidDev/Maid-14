using System.Globalization;
using Robust.Shared.IoC;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._Maid.Utils;

/// <summary>
/// A number range that can be defined as either a single float or a min/max range in YAML.
/// </summary>
[DataDefinition]
public sealed partial class RangedNumber
{
    [DataField("min")]
    public float Min = 0f;

    [DataField("max")]
    public float Max = 0f;

    public float GetValue(IRobustRandom random)
    {
        return Min + (Max - Min) * random.NextFloat();
    }
}

[TypeSerializer]
public sealed class RangedNumberSerializer : ITypeSerializer<RangedNumber, ValueDataNode>
{
    public RangedNumber Read(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<RangedNumber>? instanceProvider = null)
    {
        if (!float.TryParse(node.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            throw new ArgumentException($"Failed to parse float for RangedNumber: {node.Value}");

        return new RangedNumber { Min = val, Max = val };
    }

    public ValidationNode Validate(
        ISerializationManager serializationManager,
        ValueDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        return float.TryParse(node.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, "Failed parsing float for RangedNumber");
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        RangedNumber value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        return new ValueDataNode(value.Min.ToString(CultureInfo.InvariantCulture));
    }
}
