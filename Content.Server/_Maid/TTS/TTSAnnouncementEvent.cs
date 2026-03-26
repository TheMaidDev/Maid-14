namespace Content.Server._Maid.TTS;

/// <summary>
/// When an announcement is placed on the station, this plays TTS
/// to the nearest players using the objects specified in <see cref="TTSSystem"/>
/// </summary>
/// <param name="message">The announcement text.</param>
/// <param name="voiceId">Identifier of the TTS voice prototype to use <see cref="TTSVoicePrototype"/>.</param>
/// <param name="source">The entity that is the source of the announcement.
/// Used to determine the station when the announcement is not global.</param>
/// <param name="global">If true, the announcement is broadcast to all players.
/// If false, it is played only to players on the same station as the source entity.</param>
public sealed class TTSAnnouncementEvent(string message, string voiceId, EntityUid source, bool global)
    : EntityEventArgs
{
    public readonly string Message = message;
    public readonly bool Global = global;
    public readonly string VoiceId = voiceId;
    public readonly EntityUid Source = source;
}
