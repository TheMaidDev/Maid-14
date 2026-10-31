// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 SolsticeOfTheWinter <solsticeofthewinter@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._Maid.Graphics; // MAID Multi postshader
using Content.Shared.Nyanotrasen.Holograms;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Nyanotrasen.Holograms;

public sealed class HologramVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!; // MAID Multi postshader

    private readonly ProtoId<ShaderPrototype> _shaderId = "Holographic"; // Goobstation - Start
    private ShaderPrototype? _shaderProto;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HologramVisualsComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<HologramVisualsComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnComponentInit(EntityUid uid, HologramVisualsComponent component, ComponentInit args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetPostShader(sprite, new SpriteComponent.PostShaderArgs(MaidPostShaderIds.Hologram, (_shaderProto ??= _protoMan.Index(_shaderId)).InstanceUnique())); // MAID Multi postshader
    }

    private void OnComponentShutdown(EntityUid uid, HologramVisualsComponent component, ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.RemovePostShader(sprite, MaidPostShaderIds.Hologram); // MAID Multi postshader
    } // Goobstation - End
}
