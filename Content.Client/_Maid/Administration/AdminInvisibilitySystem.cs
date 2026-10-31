using Content.Shared._Maid.Administration;
using Robust.Client.GameObjects;

namespace Content.Client._Maid.Administration;

/// <summary>
///     Makes an invisible admin ghost translucent for those who can still see it.
/// </summary>
public sealed class AdminInvisibilitySystem : SharedAdminInvisibilitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AdminInvisibilityComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnAfterState(Entity<AdminInvisibilityComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (ent.Comp.DefaultAlpha == null)
        {
            if (!ent.Comp.Invisible)
                return;

            ent.Comp.DefaultAlpha = sprite.Color.A;
        }

        var alpha = ent.Comp.Invisible
            ? ent.Comp.DefaultAlpha.Value * ent.Comp.InvisibleAlphaMultiplier
            : ent.Comp.DefaultAlpha.Value;
        _sprite.SetColor((ent, sprite), sprite.Color.WithAlpha(alpha));
    }
}
