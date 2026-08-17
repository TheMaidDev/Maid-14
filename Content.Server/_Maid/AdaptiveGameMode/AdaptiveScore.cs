using System;
using Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Static;

namespace Content.Server._Maid.AdaptiveGameMode;

[Serializable, DataDefinition]
public partial struct AdaptiveScore
{
    [DataField("chaos")]
    public float Chaos;

    [DataField("combat")]
    public float Combat;

    public float Average => Chaos + Combat;

    public static AdaptiveScore operator +(AdaptiveScore a, AdaptiveScore b)
    {
        return new AdaptiveScore { Chaos = a.Chaos + b.Chaos, Combat = a.Combat + b.Combat };
    }

    public static AdaptiveScore operator -(AdaptiveScore a, AdaptiveScore b)
    {
        return new AdaptiveScore { Chaos = a.Chaos - b.Chaos, Combat = a.Combat - b.Combat };
    }
    public static AdaptiveScore operator *(AdaptiveScore a, float factor)
    {
        return new AdaptiveScore { Chaos = a.Chaos * factor, Combat = a.Combat * factor };
    }

    public static AdaptiveScore operator *(float factor, AdaptiveScore a)
    {
        return a * factor;
    }
    public static AdaptiveScore operator *(AdaptiveScore a, int factor)
    {
        return new AdaptiveScore { Chaos = a.Chaos * factor, Combat = a.Combat * factor };
    }

    public static AdaptiveScore operator *(int factor, AdaptiveScore a)
    {
        return a * factor;
    }
    public static explicit operator AdaptiveScore(AdaptiveScoreStaticComponent component)
    {
        return new AdaptiveScore
        {
            Chaos = component.ChaosScore.Base,
            Combat = component.CombatScore.Base,
        };
    }
    public static AdaptiveScore operator +(AdaptiveScore score, AdaptiveScoreStaticComponent component)
    {
        return new AdaptiveScore
        {
            Chaos = score.Chaos + component.ChaosScore.Base,
            Combat = score.Combat + component.CombatScore.Base,
        };
    }

    public override string ToString() => $"{{Chaos: {Chaos}, Combat: {Combat}}}";
}
