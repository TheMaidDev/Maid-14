namespace Content.Server._Maid.TTS;

public static class TtsEffects
{
    public static string Radio => "radio";
    public static string Reverse => "reverse";
    public static string Robotic => "robotic";
    public static string Echo => "echo";
    public static string Ghost => "ghost";
    public static string Announce => "announce";

    private static readonly HashSet<string> _allEffects = new()
    {
        "Radio",
        "Reverse",
        "Robotic",
        "Echo",
        "Ghost",
        "Announce"
    };

    public static bool IsValid(string? effect) =>
        effect is null || _allEffects.Contains(effect);
}
