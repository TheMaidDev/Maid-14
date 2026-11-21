using System.Text;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;

namespace Content.Client._Maid.Chat;

/// <summary>
/// Draws glyphs missing from a font prototype with the default font.
/// </summary>
public sealed class FontFallbackSystem : EntitySystem
{
    [Dependency] private readonly FontTagHijackHolder _hijack = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private static readonly ProtoId<FontPrototype> FallbackFont = "Default";
    private static readonly Rune TestRune = new('я');

    private readonly Dictionary<string, bool> _needsFallback = new();

    public override void Initialize()
    {
        base.Initialize();

        _hijack.Hijack = Hijack;
        _hijack.HijackUpdated();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _hijack.Hijack = null;
    }

    private Font? Hijack(ProtoId<FontPrototype> protoId, int size)
    {
        if (protoId == FallbackFont || !_prototype.TryIndex(protoId, out var proto))
            return null;

        var font = new VectorFont(_resourceCache.GetResource<FontResource>(proto.Path), size);

        if (!_needsFallback.TryGetValue(proto.ID, out var needsFallback))
        {
            needsFallback = font.GetCharMetrics(TestRune, 1f, fallback: false) == null;
            _needsFallback[proto.ID] = needsFallback;
        }

        if (!needsFallback)
            return font;

        var fallback = _prototype.Index(FallbackFont);
        return new StackedFont(font, new VectorFont(_resourceCache.GetResource<FontResource>(fallback.Path), size));
    }
}
