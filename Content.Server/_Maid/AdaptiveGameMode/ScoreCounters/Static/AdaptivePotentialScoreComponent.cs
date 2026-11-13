namespace Content.Server._Maid.AdaptiveGameMode.ScoreCounters.Static;

[RegisterComponent]
public sealed partial class AdaptivePotentialScoreComponent : Component
{
    public AdaptiveScore Score { get; set; }
}
