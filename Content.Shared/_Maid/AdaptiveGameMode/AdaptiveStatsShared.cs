using System;
using System.Collections.Generic;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.AdaptiveGameMode;

[Serializable, NetSerializable]
public sealed class SharedAdaptiveScoreRecord
{
    public NetEntity Entity { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Prototype { get; set; }
    public float Chaos { get; set; }
    public float Combat { get; set; }
}

[Serializable, NetSerializable]
public sealed class SharedAdaptiveCalculationRun
{
    public int Id { get; set; }
    public TimeSpan Time { get; set; }
    public float TotalChaos { get; set; }
    public float TotalCombat { get; set; }
    public float TargetChaos { get; set; }
    public float TargetCombat { get; set; }
    public List<SharedAdaptiveScoreRecord> Records { get; set; } = new();
}

[Serializable, NetSerializable]
public sealed class AdaptiveStatsEuiState(
    Dictionary<int, List<SharedAdaptiveCalculationRun>> roundData,
    int currentRoundId,
    bool trackingEnabled)
    : EuiStateBase
{
    public Dictionary<int, List<SharedAdaptiveCalculationRun>> RoundData { get; } = roundData;
    public int CurrentRoundId { get; } = currentRoundId;
    public bool TrackingEnabled { get; } = trackingEnabled;
}

[Serializable, NetSerializable]
public sealed class AdaptiveStatsToggleMessage(bool enabled) : EuiMessageBase
{
    public bool Enabled { get; } = enabled;
}

[Serializable, NetSerializable]
public sealed class AdaptiveStatsCalculateMessage : EuiMessageBase;
