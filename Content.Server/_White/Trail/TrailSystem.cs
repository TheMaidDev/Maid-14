// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._White.Trail;
using Robust.Shared.GameStates;

namespace Content.Server._White.Trail;

public sealed class TrailSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BulletTrailComponent, ComponentGetState>(OnGetState);
    }

    private void OnGetState(EntityUid uid, BulletTrailComponent component, ref ComponentGetState args)
    {
        var settings = new TrailSettings();
        TrailSettings.Inject(settings, component);
        args.State = new BulletTrailComponentState(settings);
    }
}
