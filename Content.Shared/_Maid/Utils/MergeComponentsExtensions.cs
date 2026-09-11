// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Shared._Maid.Utils;

public static class MergeComponentsExtensions
{
    /// <summary>
    /// Merges components from component registry onto existing entity,
    /// like it works with "parent" field when defining entity prototype.
    /// <remarks>This method kinda heavy and does unnecessary copying. Use with caution</remarks>
    /// </summary>
    public static void MergeComponents(
        this IEntityManager entMan,
        EntityUid uid,
        ComponentRegistry registry,
        ISerializationManager? serialization = null,
        bool dirty = true)
    {
        serialization ??= IoCManager.Resolve<ISerializationManager>();

        foreach (var (_, entry) in registry)
        {
            if (!entMan.TryGetComponent(uid, entry.Component.GetType(), out var existingComp))
            {
                entMan.AddComponent(uid, entMan.ComponentFactory.GetComponent(entry));
                continue;
            }

            // This component already exists so we merge them

            // Yeah, we doing a lot of copying there, but i don't see other ways without manual serialization out of sandbox
            var existingComponentNode = (MappingDataNode) serialization.WriteValue(existingComp.GetType(), existingComp);

            var mergedComponentNode = serialization.CombineMappings(entry.Mapping, existingComponentNode);

            var mergedComp = (IComponent) serialization.Read(existingComp.GetType(), mergedComponentNode, notNullableOverride: true)!;

            object? target = existingComp;
            serialization.CopyTo(mergedComp, ref target);

            if (dirty)
                entMan.Dirty(uid, existingComp);
        }
    }
}
