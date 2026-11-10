using System.Buffers.Binary;

namespace Content.Shared._Maid.PlayerMusic;

/// <summary>
/// What we learned about an Ogg Vorbis file without decoding it.
/// </summary>
public readonly record struct PlayerMusicOggInfo(int Channels, int SampleRate, long Samples)
{
    public double Seconds => (double) Samples / SampleRate;

    /// <summary>
    /// Size of the whole decoded stream. The engine decodes the entire file to 16-bit PCM on load.
    /// </summary>
    public long DecodedBytes => Samples * Channels * 2;
}

/// <summary>
/// Cheap structural validation of Ogg Vorbis files. Walks every Ogg page, so truncated or
/// broken files are rejected before they are stored or sent to other players.
/// </summary>
public static class PlayerMusicOgg
{
    private const int MinSampleRate = 8000;
    private const int MaxSampleRate = 48000;
    private const int MaxPages = 200_000;

    public static bool TryAnalyze(ReadOnlySpan<byte> d, out PlayerMusicOggInfo info)
    {
        info = default;

        var pos = 0;
        var pages = 0;
        var first = true;
        var ended = false;
        uint serial = 0;
        var channels = 0;
        var rate = 0;
        long lastGranule = -1;

        while (pos < d.Length)
        {
            // Nothing is allowed after the last page (this also rejects chained streams).
            if (ended || ++pages > MaxPages)
                return false;

            if (d.Length - pos < 27)
                return false;

            if (d[pos] != (byte) 'O' || d[pos + 1] != (byte) 'g' || d[pos + 2] != (byte) 'g' || d[pos + 3] != (byte) 'S')
                return false;

            if (d[pos + 4] != 0)
                return false;

            var flags = d[pos + 5];
            var granule = BinaryPrimitives.ReadInt64LittleEndian(d.Slice(pos + 6, 8));
            var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(pos + 14, 4));
            var segments = d[pos + 26];
            var headerLength = 27 + segments;

            if (d.Length - pos < headerLength)
                return false;

            var bodyLength = 0;
            for (var i = 0; i < segments; i++)
            {
                bodyLength += d[pos + 27 + i];
            }

            if (d.Length - pos - headerLength < bodyLength)
                return false;

            var body = d.Slice(pos + headerLength, bodyLength);

            if (first)
            {
                // First page must be the beginning of the stream with the Vorbis identification header
                if ((flags & 0x02) == 0 || body.Length < 30)
                    return false;

                if (body[0] != 1 ||
                    body[1] != (byte) 'v' || body[2] != (byte) 'o' || body[3] != (byte) 'r' ||
                    body[4] != (byte) 'b' || body[5] != (byte) 'i' || body[6] != (byte) 's')
                {
                    return false;
                }

                // Vorbis version must be 0
                if (BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(7, 4)) != 0)
                    return false;

                channels = body[11];
                rate = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(12, 4));
                serial = pageSerial;
                first = false;
            }
            else
            {
                // A second logical stream (chained file) is not allowed
                if ((flags & 0x02) != 0 || pageSerial != serial)
                    return false;
            }

            if ((flags & 0x04) != 0)
                ended = true;

            // -1 means that no packet ends on this page
            if (granule >= 0)
            {
                if (granule < lastGranule)
                    return false;

                lastGranule = granule;
            }

            pos += headerLength + bodyLength;
        }

        // The last page has to be marked as the end of the stream, otherwise the file is cut off
        if (first || !ended || lastGranule <= 0)
            return false;

        if (channels is < 1 or > 2 || rate is < MinSampleRate or > MaxSampleRate)
            return false;

        info = new PlayerMusicOggInfo(channels, rate, lastGranule);
        return true;
    }
}
