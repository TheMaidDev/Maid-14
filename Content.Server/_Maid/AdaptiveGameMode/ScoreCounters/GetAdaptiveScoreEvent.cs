using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters;

/// <summary>
/// Raised as a broadcast event to calculate the current total adaptive chaos score.
/// </summary>
[ByRefEvent]
public struct GetAdaptiveScoreEvent()
{
    public float ChaosScore = 0f;
    public float CombatScore = 0f;

    /// <summary>
    /// Stored list of score records added during the calculation. Defined only if tracking is enabled.
    /// </summary>
    public List<AdaptiveScoreRecord>? Records = null;

    [Obsolete("Use overload with EntityUid instead")]
    public void Add(float chaos, float combat)
    {
        Add(EntityUid.Invalid, chaos, combat);
    }

    [Obsolete("Use overload with EntityUid instead")]
    public void Add(float score) => Add(score, score);

    public void Add(EntityUid uid, float chaos, float combat)
    {
        ChaosScore += chaos;
        CombatScore += combat;

        if (Records != null)
        {
            Records.Add(new AdaptiveScoreRecord(uid, chaos, combat));
        }
    }

    public void Add(EntityUid uid, float score) => Add(uid, score, score);

    public float Average => (ChaosScore + CombatScore) / 2f;
}

public record struct AdaptiveScoreRecord(EntityUid Entity, float Chaos, float Combat);
