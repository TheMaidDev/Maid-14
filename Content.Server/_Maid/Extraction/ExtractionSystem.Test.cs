using Content.Shared._Maid.Extraction;

namespace Content.Server._Maid.Extraction;

/// <summary>
/// Drop pod landing test marker.
/// </summary>
public sealed partial class ExtractionSystem
{
    private readonly List<EntityUid> _landingTests = new();

    private void InitializeTest()
    {
        SubscribeLocalEvent<DropPodLandingTestComponent, MapInitEvent>(OnLandingTestMapInit);
    }

    private void OnLandingTestMapInit(Entity<DropPodLandingTestComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.LandAt = _timing.CurTime + ent.Comp.Delay;
    }

    private void UpdateLandingTests(TimeSpan now)
    {
        var query = EntityQueryEnumerator<DropPodLandingTestComponent>();
        while (query.MoveNext(out var uid, out var test))
        {
            if (now >= test.LandAt)
                _landingTests.Add(uid);
        }

        foreach (var uid in _landingTests)
        {
            var test = Comp<DropPodLandingTestComponent>(uid);
            if (LandDropPod(Transform(uid).Coordinates, test.Landing) is { } pod
                && TryComp<DropPodComponent>(pod, out var podComp))
            {
                // empty and open, nobody has its code
                podComp.Locked = false;
                Dirty(pod, podComp);
                SetGlow((pod, podComp), DropPodGlow.Unlocked);
            }

            QueueDel(uid);
        }

        _landingTests.Clear();
    }
}
