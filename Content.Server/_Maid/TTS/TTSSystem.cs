using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Goobstation.Common.Traits;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Chat.Systems;
using Content.Server.Light.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._Maid.CVars;
using Content.Shared._Maid.TTS;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Maid.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    private const int MaxMessageChars = 400;
    private bool _isEnabled;


    public override void Initialize()
    {
        _cfg.OnValueChanged(MaidCVars.TTSEnabled, v => _isEnabled = v, true);

        SubscribeLocalEvent<TTSComponent, EntitySpokeEvent>(OnEntitySpoke);

        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _ttsManager.ResetCache());
        SubscribeLocalEvent<TTSAnnouncementEvent>(OnAnnounceRequest);
        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);
    }

    private async void OnAnnounceRequest(TTSAnnouncementEvent ev)
    {
        if (!_isEnabled || !_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var ttsPrototype))
            return;

        var message = FormattedMessage.RemoveMarkupOrThrow(ev.Message);
        var soundData = await GenerateTTS(message, ttsPrototype.Speaker, effect: TtsEffects.Announce);

        if (soundData == null)
            return;

        Filter filter;
        if (ev.Global)
        {
            filter = Filter.Broadcast();
        }
        else
        {
            var station = _station.GetOwningStation(ev.Source);
            if (station == null)
                return;

            if (!EntityManager.TryGetComponent<StationDataComponent>(station, out var stationDataComp))
                return;

            filter = _station.GetInStation(stationDataComp);
        }

        foreach (var player in filter.Recipients)
        {
            if (player.AttachedEntity == null)
                continue;

            // Get emergency lights in range to broadcast from
            var entities = _lookup.GetEntitiesInRange(player.AttachedEntity.Value, 30f)
                .Where(HasComp<EmergencyLightComponent>)
                .ToList();

            if (entities.Count == 0)
                continue;

            // Get closest emergency light
            var entity = entities.First();
            var range = new Vector2(100f);

            foreach (var item in entities)
            {
                var itemSource = _xforms.GetWorldPosition(Transform(item));
                var playerSource = _xforms.GetWorldPosition(Transform(player.AttachedEntity.Value));

                var distance = playerSource - itemSource;

                if (range.Length() > distance.Length())
                {
                    range = distance;
                    entity = item;
                }
            }

            RaiseNetworkEvent(new PlayTTSEvent(soundData, GetNetEntity(entity), false), Filter.SinglePlayer(player), false);
        }
    }

    private async void OnEntitySpoke(EntityUid uid, TTSComponent component, EntitySpokeEvent args)
    {
        if (!_isEnabled || args.Message.Length > MaxMessageChars)
            return;

        if (!args.Language.SpeechOverride.RequireSpeech)
            return;

        var voiceId = component.VoicePrototypeId;
        var voiceEv = new TransformSpeakerVoiceEvent(uid, voiceId);
        RaiseLocalEvent(uid, voiceEv);
        voiceId = voiceEv.VoiceId;

        if (!_prototypeManager.TryIndex(voiceId, out var protoVoice))
            return;

        if (args.IsWhisper)
        {
            HandleWhisper(uid, args.Message, args.Language, protoVoice.Speaker);
            return;
        }

        HandleSay(uid, args.Message, args.Language, protoVoice.Speaker);
    }

    private async void HandleSay(EntityUid uid, string message, LanguagePrototype language, string speaker)
    {
        var normal = await GenerateTTS(message, speaker);
        if (normal is null)
            return;

        var obfuscated = await GenerateTTS(_language.ObfuscateSpeech(message, language), speaker);
        if (obfuscated is null)
            return;

        var nilter = Filter.Empty();
        var lilter = Filter.Empty();
        foreach (var session in Filter.Pvs(uid).Recipients)
        {
            if (!session.AttachedEntity.HasValue)
                continue;

            if (EntityManager.HasComponent<DeafComponent>(session.AttachedEntity.Value))
                continue;

            EntityManager.TryGetComponent(session.AttachedEntity.Value, out LanguageSpeakerComponent? lang);
            if (_language.CanUnderstand(new(session.AttachedEntity.Value, lang), language.ID))
                nilter.AddPlayer(session);
            else
                lilter.AddPlayer(session);
        }

        RaiseNetworkEvent(new PlayTTSEvent(normal, GetNetEntity(uid)), nilter);
        RaiseNetworkEvent(new PlayTTSEvent(obfuscated, GetNetEntity(uid)), lilter, false);
    }

    private async void HandleWhisper(EntityUid uid, string message, LanguagePrototype language, string speaker)
    {
        var normal = await GenerateTTS(message, speaker, true);
        if (normal is null)
            return;

        var obfuscated = await GenerateTTS(_language.ObfuscateSpeech(message, language), speaker, true);
        if (obfuscated is null)
            return;

        // TODO: Check obstacles
        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourcePos = _xforms.GetWorldPosition(xformQuery.GetComponent(uid), xformQuery);
        var nilter = Filter.Empty();
        var lilter = Filter.Empty();
        foreach (var session in Filter.Pvs(uid).Recipients)
        {
            if (!session.AttachedEntity.HasValue)
                continue;

            var xform = xformQuery.GetComponent(session.AttachedEntity.Value);
            var distance = (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();
            if (distance > ChatSystem.WhisperMuffledRange)
                continue;

            EntityManager.TryGetComponent(session.AttachedEntity.Value, out LanguageSpeakerComponent? lang);
            if (_language.CanUnderstand(new(session.AttachedEntity.Value, lang), language.ID)
                && distance <= ChatSystem.WhisperClearRange)
                nilter.AddPlayer(session);
            else
                lilter.AddPlayer(session);
        }

        RaiseNetworkEvent(new PlayTTSEvent(normal, GetNetEntity(uid), true), nilter);
        RaiseNetworkEvent(new PlayTTSEvent(obfuscated, GetNetEntity(uid), true), lilter, false);
    }

    private readonly Dictionary<string, Task<byte[]?>> _ttsTasks = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // ReSharper disable once InconsistentNaming
    private async Task<byte[]?> GenerateTTS(string text, string speaker, bool isWhisper = false, string? effect = null)
    {
        var textSanitized = Sanitize(text);
        if (string.IsNullOrEmpty(textSanitized))
            return null;

        if (char.IsLetter(textSanitized[^1]))
            textSanitized += ".";

        var taskKey = $"{textSanitized}_{speaker}_{isWhisper}";

        await _lock.WaitAsync();
        try
        {
            if (_ttsTasks.TryGetValue(taskKey, out var existingTask))
                return await existingTask;

            var newTask = _ttsManager.ConvertTextToSpeech(speaker, textSanitized, effect);
            _ttsTasks[taskKey] = newTask;
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            return await _ttsTasks[taskKey];
        }
        finally
        {
            await _lock.WaitAsync();
            try
            {
                _ttsTasks.Remove(taskKey);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}

