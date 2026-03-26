using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Maid.CVars;
using Prometheus;
using Robust.Shared.Configuration;

namespace Content.Server._Maid.TTS;


// ReSharper disable once InconsistentNaming
public sealed class TTSManager
{
    private static readonly Histogram RequestTimings = Metrics.CreateHistogram(
        "tts_req_timings",
        "Timings of TTS API requests",
        new HistogramConfiguration()

        {
            LabelNames = new[] {"type"},
            Buckets = Histogram.ExponentialBuckets(.1, 1.5, 10),
        });

    private static readonly Counter WantedCount = Metrics.CreateCounter(
        "tts_wanted_count",
        "Amount of wanted TTS audio.");

    private static readonly Counter ReusedCount = Metrics.CreateCounter(
        "tts_reused_count",
        "Amount of reused TTS audio from cache.");

    [Robust.Shared.IoC.Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly HttpClient _httpClient = new();

    private ISawmill _sawmill = default!;

    private readonly Dictionary<string, byte[]> _cache = new();
    private readonly HashSet<string> _cacheKeysSeq = new();
    private int _maxCachedCount = 200;

    public IReadOnlyDictionary<string, byte[]> Cache => _cache;
    public IReadOnlyCollection<string> CacheKeysSeq => _cacheKeysSeq;
    public int MaxCachedCount
    {
        get => _maxCachedCount;
        set
        {
            _maxCachedCount = value;
            ResetCache();
        }
    }

    private string _apiUrl = string.Empty;
    private string _apiToken = string.Empty;

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("tts");

        _cfg.OnValueChanged(MaidCVars.TTSMaxCache, val =>
        {
            _maxCachedCount = val;
            ResetCache();
        }, true);
        _cfg.OnValueChanged(MaidCVars.TTSApiUrl, v => _apiUrl = v, true);
        _cfg.OnValueChanged(MaidCVars.TTSApiToken, v => _apiToken = v, true);
    }

    /// <summary>
    /// Generates audio with passed text by API
    /// </summary>
    /// <param name="speaker">Identifier of speaker</param>
    /// <param name="text">Formatted text</param>
    /// <returns>OGG audio bytes or null if failed</returns>
    public async Task<byte[]?> ConvertTextToSpeech(string speaker, string text, string? effect = null)
    {
        WantedCount.Inc();
        var cacheKey = GenerateCacheKey(speaker, text, effect);
        if (_cache.TryGetValue(cacheKey, out var data))
        {
            ReusedCount.Inc();
            _sawmill.Verbose($"Use cached sound for '{text}' speech by '{speaker}' speaker");
            return data;
        }

        _sawmill.Verbose($"Generate new audio for '{text}' speech by '{speaker}' speaker");

        var validatedEffect = TtsEffects.IsValid(effect) ? effect : null;
        var reqTime = DateTime.UtcNow;
        try
        {
            var timeout = _cfg.GetCVar(MaidCVars.TTSApiTimeout);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

            var queryParams = System.Web.HttpUtility.ParseQueryString(string.Empty);
            queryParams["speaker"] = speaker;
            queryParams["text"] = text;
            queryParams["ext"] = "ogg";
            queryParams["effect"] = validatedEffect;

            var url = $"{_apiUrl}?{queryParams}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);

            var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _sawmill.Warning("TTS request was rate limited");
                    return null;
                }

                _sawmill.Error($"TTS request returned bad status code: {response.StatusCode}");
                return null;
            }

            var soundData = await response.Content.ReadAsByteArrayAsync(cts.Token);

            _cache.TryAdd(cacheKey, soundData);
            _cacheKeysSeq.Add(cacheKey);

            while (_cache.Count > _maxCachedCount && _cacheKeysSeq.Count > 0)
            {
                var oldestKey = _cacheKeysSeq.First();
                _cache.Remove(oldestKey);
                _cacheKeysSeq.Remove(oldestKey);
            }

            _sawmill.Debug($"Generated new audio for '{text}' speech by '{speaker}' speaker ({soundData.Length} bytes)");
            RequestTimings.WithLabels("Success").Observe((DateTime.UtcNow - reqTime).TotalSeconds);

            return soundData;
        }
        catch (TaskCanceledException)
        {
            RequestTimings.WithLabels("Timeout").Observe((DateTime.UtcNow - reqTime).TotalSeconds);
            _sawmill.Error($"Timeout of request generation new audio for '{text}' speech by '{speaker}' speaker");
            return null;
        }
        catch (Exception e)
        {
            RequestTimings.WithLabels("Error").Observe((DateTime.UtcNow - reqTime).TotalSeconds);
            _sawmill.Error($"Failed of request generation new sound for '{text}' speech by '{speaker}' speaker\n{e}");
            return null;
        }
    }


    public void ResetCache()
    {
        _cache.Clear();
        _cacheKeysSeq.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private string GenerateCacheKey(string speaker, string text, string? effect)
    {
        var keyData = Encoding.UTF8.GetBytes($"{speaker}/{text}/{effect}");
        var hashBytes = System.Security.Cryptography.SHA256.HashData(keyData);
        return Convert.ToHexString(hashBytes);
    }
}
