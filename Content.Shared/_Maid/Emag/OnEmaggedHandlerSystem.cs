using Content.Shared.Emag.Systems;

namespace Content.Shared._Maid.Emag;

/// <summary>
/// The usual emag boilerplate: accept the matching flag, refuse if it is already set.
/// </summary>
public sealed class OnEmaggedHandlerSystem : EntitySystem
{
    [Dependency] private readonly EmagSystem _emag = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OnEmaggedHandlerComponent, GotEmaggedEvent>(OnEmagged);
    }

    private void OnEmagged(Entity<OnEmaggedHandlerComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, ent.Comp.Flag))
            return;

        if (_emag.CheckFlag(ent, ent.Comp.Flag))
            return;

        args.Handled = true;
    }
}
