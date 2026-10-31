// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 deltanedas <@deltanedas:kde.org>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Enchanting.Components;
using Content.Goobstation.Shared.Enchanting.Systems;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Goobstation.Client.Enchanting.Systems;

/// <summary>
/// Gives enchanted items a cool shader
/// </summary>
public sealed class EnchantVisualsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!; // MAID rt-update

    private const string PostShaderId = "enchant"; // MAID rt-update

    public readonly ProtoId<ShaderPrototype> Shader = "Enchant";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnchantedComponent, ComponentStartup>(OnStartup); // MAID rt-update
        SubscribeLocalEvent<EnchantedComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<EnchantedComponent, HeldVisualsUpdatedEvent>(OnHeldVisualsUpdated);
        SubscribeLocalEvent<EnchantedComponent, EquipmentVisualsUpdatedEvent>(OnEquipmentVisualsUpdated);
    }

    // MAID BEGIN rt-update sprite post-shaders are keyed by id since RT 286 and must not be set before the sprite is initialized
    private void OnStartup(Entity<EnchantedComponent> ent, ref ComponentStartup args)
    {
        ApplyShader(ent);
    }

    private void OnHandleState(Entity<EnchantedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyShader(ent);
    }

    private void ApplyShader(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || !sprite.Initialized)
            return;

        if (_sprite.HasPostShader((uid, sprite), PostShaderId))
            return;

        _sprite.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(PostShaderId, _proto.Index(Shader).InstanceUnique()));
    }
    // MAID END rt-update

    private void OnHeldVisualsUpdated(Entity<EnchantedComponent> ent, ref HeldVisualsUpdatedEvent args)
    {
        SetLayers(args.User, args.RevealedLayers);
    }

    private void OnEquipmentVisualsUpdated(Entity<EnchantedComponent> ent, ref EquipmentVisualsUpdatedEvent args)
    {
        SetLayers(args.Equipee, args.RevealedLayers);
    }

    private void SetLayers(EntityUid uid, HashSet<string> keys)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        foreach (var key in keys)
        {
            if (sprite.LayerMapTryGet(key, out var index))
                sprite.LayerSetShader(index, Shader);
        }
    }
}
