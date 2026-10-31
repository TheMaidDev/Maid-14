using Content.Shared._Maid.Administration;
using Content.Shared.Actions;
using Content.Shared.Eye;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Server.GameObjects;

namespace Content.Server._Maid.Administration;

public sealed class AdminInvisibilitySystem : SharedAdminInvisibilitySystem
{
    [Dependency] private readonly FollowerSystem _follower = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly VisibilitySystem _visibility = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AdminInvisibilityComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AdminInvisibilityComponent, GetVisMaskEvent>(OnGetVisMask);
        SubscribeLocalEvent<AdminInvisibilityComponent, ToggleAdminInvisibilityActionEvent>(OnToggleAction);
    }

    private void OnStartup(Entity<AdminInvisibilityComponent> ent, ref ComponentStartup args)
    {
        _eye.RefreshVisibilityMask(ent.Owner);
    }

    protected override void OnShutdown(Entity<AdminInvisibilityComponent> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);

        if (Terminating(ent.Owner))
            return;

        _eye.RefreshVisibilityMask(ent.Owner);
    }

    private void OnGetVisMask(Entity<AdminInvisibilityComponent> ent, ref GetVisMaskEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.VisibilityMask |= (int) VisibilityFlags.Admin;
    }

    private void OnToggleAction(Entity<AdminInvisibilityComponent> ent, ref ToggleAdminInvisibilityActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetInvisible(ent, !ent.Comp.Invisible);

        _popup.PopupEntity(Loc.GetString(ent.Comp.Invisible ? "admin-invisibility-on" : "admin-invisibility-off"), ent, ent);
    }

    public void SetInvisible(Entity<AdminInvisibilityComponent> ent, bool invisible)
    {
        if (ent.Comp.Invisible == invisible)
            return;

        var visibility = EnsureComp<VisibilityComponent>(ent);

        if (invisible)
        {
            // Followers would give away where we are.
            if (TryComp<FollowedComponent>(ent, out var followed))
                _follower.StopAllFollowers(ent, followed);

            _visibility.RemoveLayer((ent, visibility), (ushort) VisibilityFlags.Ghost, false);
            _visibility.RemoveLayer((ent, visibility), (ushort) VisibilityFlags.Normal, false);
            _visibility.AddLayer((ent, visibility), (ushort) VisibilityFlags.Admin, false);
        }
        else
        {
            _visibility.RemoveLayer((ent, visibility), (ushort) VisibilityFlags.Admin, false);
            var layer = HasComp<GhostComponent>(ent) ? VisibilityFlags.Ghost : VisibilityFlags.Normal;
            _visibility.AddLayer((ent, visibility), (ushort) layer, false);
        }

        _visibility.RefreshVisibility(ent, visibilityComponent: visibility);

        ent.Comp.Invisible = invisible;
        Dirty(ent);

        _actions.SetToggled(ent.Comp.ToggleActionEntity, invisible);
    }
}
