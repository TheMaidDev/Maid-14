// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Shared._Maid.Utils;

/// <summary>
/// Replacement for the RT <c>EntityUid.SpawnTimer</c>/<c>SpawnRepeatingTimer</c> extensions that were removed together
/// with <c>TimerComponent</c> in RT 275. Timers are no longer owned by the entity, the callback is just skipped
/// if the entity is gone when it fires. Prefer a proper update loop / <c>TimeSpan</c> field for new code.
/// </summary>
public static class EntityTimerExtensions
{
    public static void SpawnTimer(this EntityUid uid, int milliseconds, Action onFired, CancellationToken cancellationToken = default)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        Timer.Spawn(milliseconds, () =>
        {
            if (entMan.Deleted(uid))
                return;

            onFired();
        }, cancellationToken);
    }

    public static void SpawnTimer(this EntityUid uid, TimeSpan delay, Action onFired, CancellationToken cancellationToken = default)
    {
        uid.SpawnTimer((int) delay.TotalMilliseconds, onFired, cancellationToken);
    }

    public static void SpawnRepeatingTimer(this EntityUid uid, TimeSpan interval, Action onFired, CancellationToken cancellationToken)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        Timer.SpawnRepeating(interval, () =>
        {
            if (entMan.Deleted(uid))
                return;

            onFired();
        }, cancellationToken);
    }
}
