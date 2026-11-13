using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Shared._Maid.Prototypes;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.YAMLLinter._Maid;

/// <summary>
/// Validates /Prototypes while working around a RobustToolbox bug: <c>ValidateDirectory</c> parses
/// files with <c>MappingDataNode(YamlMappingNode)</c>, which drops mapping key tags, so partial
/// sequence items tagged <c>!CombineIndex:N</c> on their first key get appended instead of merged
/// and crash or fail validation as incomplete entries.
/// </summary>
/// <remarks>
/// Validation runs on an in-memory mirror of /Prototypes in which those items are removed from
/// partial files. Runtime loading merges them correctly and is still checked by the linter's
/// client/server startup. Remove this once the engine keeps key tags in that constructor.
/// </remarks>
public static class CombineIndexValidation
{
    private const string CombineIndexTag = "!CombineIndex:";

    private static readonly ResPath Prototypes = new("/Prototypes");
    private static readonly ResPath MirrorRoot = new("/_MaidLinter");
    private static readonly ResPath MirrorPrototypes = MirrorRoot / "Prototypes";

    public static Dictionary<string, HashSet<ErrorNode>> ValidatePrototypes(
        IPrototypeManager prototypeManager,
        IResourceManager resourceManager,
        out Dictionary<Type, HashSet<string>> prototypes)
    {
        var mirror = new MemoryContentRoot();
        var mirrorFiles = new List<ResPath>();

        foreach (var file in resourceManager.ContentFindFiles(Prototypes))
        {
            if (file.Extension != "yml")
                continue;

            using var reader = new StreamReader(resourceManager.ContentFileRead(file));
            var text = reader.ReadToEnd();
            if (MaidPartialPrototypes.IsPartial(Prototypes, file))
                text = StripCombineIndexItems(text);

            var relative = file.RelativeTo(Prototypes);
            mirror.AddOrUpdateFile(new ResPath("Prototypes") / relative, text);
            mirrorFiles.Add(MirrorPrototypes / relative);
        }

        resourceManager.AddRoot(MirrorRoot, mirror);
        MaidPartialPrototypes.Mark(prototypeManager, MirrorPrototypes, mirrorFiles);

        var errors = prototypeManager.ValidateDirectory(MirrorPrototypes, out prototypes);
        return errors.ToDictionary(pair => ToOriginalPath(pair.Key), pair => pair.Value);
    }

    private static string ToOriginalPath(string mirrorPath)
    {
        var root = MirrorRoot.ToString();
        return mirrorPath.StartsWith(root) ? mirrorPath[root.Length..] : mirrorPath;
    }

    private static string StripCombineIndexItems(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));

        var changed = false;
        foreach (var document in stream.Documents)
        {
            changed |= Strip(document.RootNode);
        }

        if (!changed)
            return text;

        var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static bool Strip(YamlNode node)
    {
        var changed = false;
        switch (node)
        {
            case YamlSequenceNode sequence:
                for (var i = sequence.Children.Count - 1; i >= 0; i--)
                {
                    if (IsCombineIndexItem(sequence.Children[i]))
                    {
                        sequence.Children.RemoveAt(i);
                        changed = true;
                    }
                    else
                    {
                        changed |= Strip(sequence.Children[i]);
                    }
                }
                break;

            case YamlMappingNode mapping:
                foreach (var (_, value) in mapping.Children)
                {
                    changed |= Strip(value);
                }
                break;
        }

        return changed;
    }

    private static bool IsCombineIndexItem(YamlNode node)
    {
        return node is YamlMappingNode { Children.Count: > 0 } mapping
               && mapping.Children.First().Key is YamlScalarNode { Tag.IsEmpty: false } key
               && key.Tag.Value.StartsWith(CombineIndexTag);
    }
}
