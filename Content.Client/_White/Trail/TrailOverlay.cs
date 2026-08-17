// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._White.Trail.Line.Manager;
using Content.Shared._White;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._White.Trail;

public sealed class TrailOverlay : Overlay
{
    private readonly IPrototypeManager _protoManager;
    private readonly IResourceCache _cache;
    private readonly ITrailLineManager _lineManager;
    private readonly IConfigurationManager _cfg;

    private bool _showTrails;

    private readonly Dictionary<string, ShaderInstance?> _shaderDict;
    private readonly Dictionary<string, Texture?> _textureDict;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public TrailOverlay(
        IPrototypeManager protoManager,
        IResourceCache cache,
        IConfigurationManager cfg,
        ITrailLineManager lineManager
    )
    {
        _protoManager = protoManager;
        _cache = cache;
        _cfg = cfg;
        _lineManager = lineManager;

        _cfg.OnValueChanged(WhiteCVars.ShowTrails, val => _showTrails = val, true);

        _shaderDict = new Dictionary<string, ShaderInstance?>();
        _textureDict = new Dictionary<string, Texture?>();

        ZIndex = (int) Shared.DrawDepth.DrawDepth.Effects;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var item in _lineManager.Lines)
        {
            if (!_showTrails && item.Settings.OptionsConcealable)
                continue;

            item.Render(handle, GetCachedTexture(item.Settings.TexurePath ?? ""));
        }
    }

    private ShaderInstance? GetCachedShader(string id)
    {
        if (_shaderDict.TryGetValue(id, out var shader))
            return shader;

        if (_protoManager.TryIndex<ShaderPrototype>(id, out var shaderRes))
            shader = shaderRes?.InstanceUnique();

        _shaderDict.Add(id, shader);
        return shader;
    }

    private Texture? GetCachedTexture(string path)
    {
        if (_textureDict.TryGetValue(path, out var texture))
            return texture;

        if (_cache.TryGetResource<TextureResource>(path, out var texRes))
            texture = texRes;

        _textureDict.Add(path, texture);
        return texture;
    }
}
