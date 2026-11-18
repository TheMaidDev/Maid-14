using System.Diagnostics.CodeAnalysis;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Maid.Structures;

public sealed class ProtectedByWindowSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProtectedByWindowComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<ProtectedByWindowComponent, MeleeDamageRedirectEvent>(OnMeleeRedirect);
        SubscribeLocalEvent<ProtectedByWindowComponent, PreventCollideEvent>(OnPreventCollide);
    }

    // Перенаправляем мили-удар на окно до нанесения урона.
    private void OnMeleeRedirect(Entity<ProtectedByWindowComponent> ent, ref MeleeDamageRedirectEvent args)
    {
        if (TryGetWindow(ent, out var window))
            args.NewTarget = window;
    }

    private void OnBeforeDamageChanged(Entity<ProtectedByWindowComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || !TryGetWindow(ent, out var window))
            return;

        args.Cancelled = true;

        if (args.Origin != null)
            _damageable.TryChangeDamage(window, args.Damage, origin: args.Origin);
    }

    private void OnPreventCollide(Entity<ProtectedByWindowComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasComp<ProjectileComponent>(args.OtherEntity) && !HasComp<ThrownItemComponent>(args.OtherEntity))
            return;

        if (TryGetWindow(ent, out _))
            args.Cancelled = true;
    }

    private bool TryGetWindow(Entity<ProtectedByWindowComponent> ent, [NotNullWhen(true)] out EntityUid? window)
    {
        window = null;
        var xform = Transform(ent);

        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);

        while (anchored.MoveNext(out var uid))
        {
            if (uid == ent.Owner || !_whitelist.IsWhitelistPass(ent.Comp.Whitelist, uid.Value))
                continue;

            window = uid;
            return true;
        }

        return false;
    }
}
