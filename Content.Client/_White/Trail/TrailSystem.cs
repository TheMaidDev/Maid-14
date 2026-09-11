// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._White.Trail.Line.Manager;
using Content.Shared._White.Trail;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._White.Trail;

public sealed class TrailSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly ITrailLineManager _lineManager = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlayManager.AddOverlay(
            new TrailOverlay(
                IoCManager.Resolve<IPrototypeManager>(),
                IoCManager.Resolve<IResourceCache>(),
                IoCManager.Resolve<IConfigurationManager>(),
                _lineManager
            ));

        SubscribeLocalEvent<BulletTrailComponent, MoveEvent>(OnTrailMove);
        SubscribeLocalEvent<BulletTrailComponent, ComponentRemove>(OnTrailRemove);
        SubscribeLocalEvent<BulletTrailComponent, ComponentHandleState>(OnHandleState);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<TrailOverlay>();
    }

    private void OnHandleState(EntityUid uid, BulletTrailComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not BulletTrailComponentState state)
            return;

        TrailSettings.Inject(component, state.Settings);
    }

    private void OnTrailRemove(EntityUid uid, BulletTrailComponent comp, ComponentRemove args)
    {
        _lineManager.Detach(comp);
    }

    private void OnTrailMove(EntityUid uid, BulletTrailComponent comp, ref MoveEvent args)
    {
        if (comp.СreationMethod != SegmentCreationMethod.OnMove || _gameTiming.InPrediction)
            return;

        TryCreateSegment(comp, args.Component);
    }

    private void TryCreateSegment(BulletTrailComponent comp, TransformComponent xform)
    {
        if (!comp.Enabled || xform.MapID == MapId.Nullspace)
            return;

        comp.TrailLine ??= _lineManager.CreateTrail(comp, xform.MapID);
        comp.TrailLine.TryCreateSegment(_transformSystem.GetWorldPositionRotation(xform), xform.MapID);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _lineManager.Update(frameTime);

        foreach (var (comp, xform) in EntityQuery<BulletTrailComponent, TransformComponent>())
        {
            if (comp.СreationMethod == SegmentCreationMethod.OnFrameUpdate)
                TryCreateSegment(comp, xform);
        }
    }
}
