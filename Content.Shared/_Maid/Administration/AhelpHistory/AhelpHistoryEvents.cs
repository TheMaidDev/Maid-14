using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Administration.AhelpHistory
{
    [Serializable, NetSerializable]
    public sealed class AhelpHistoryEntry
    {
        public int Id { get; set; }
        public DateTime SentAt { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public bool SenderIsAdmin { get; set; }
        public bool FromWebhook { get; set; }
        public bool AdminOnly { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? RoundId { get; set; }

        public string RoundServerName { get; set; } = string.Empty;
        public bool RoundStartedAtKnown { get; set; }
        public DateTime RoundStartedAt { get; set; }
    }

    [Serializable, NetSerializable]
    public sealed class AhelpHistoryRequest : EntityEventArgs
    {
        public NetUserId Target { get; set; }
        public int Limit { get; set; }

        public int Skip { get; set; }

        public int? AnchorId { get; set; }

        public int? RoundId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Text { get; set; }
    }

    [Serializable, NetSerializable]
    public sealed class AhelpHistoryResponse : EntityEventArgs
    {
        public NetUserId Target { get; set; }
        public string TargetName { get; set; } = string.Empty;
        public List<AhelpHistoryEntry> Entries { get; set; } = new();
        public bool HasMore { get; set; }

        public int AnchorSkip { get; set; }

        public string? Error { get; set; }
    }

    [Serializable, NetSerializable]
    public sealed class OpenAhelpHistory : EntityEventArgs
    {
        public NetUserId Target { get; set; }
        public string TargetName { get; set; } = string.Empty;
    }
}
