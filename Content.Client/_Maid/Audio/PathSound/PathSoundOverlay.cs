using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._Maid.Audio.PathSound;

/// <summary>Display mode of the sound path debug overlay.</summary>
public enum PathSoundOverlayMode
{
    Off,
    Field,
    Numbers,
    All,
}

/// <summary>
///     Debug overlay answering "where and how loud should I hear sound": tiles are colored by the
///     audibility the field predicts, with optional per-tile numbers, managed sound markers and a legend.
/// </summary>
public sealed class PathSoundOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    private readonly PathSoundSystem _system;
    private readonly SharedTransformSystem _transform;
    private readonly SharedMapSystem _mapSystem;
    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    public PathSoundOverlay(PathSoundSystem system)
    {
        IoCManager.InjectDependencies(this);

        _system = system;
        _transform = _entManager.System<SharedTransformSystem>();
        _mapSystem = _entManager.System<SharedMapSystem>();

        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 8);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        switch (args.Space)
        {
            case OverlaySpace.ScreenSpace:
                DrawScreen(args);
                break;
            case OverlaySpace.WorldSpace:
                DrawWorld(args);
                break;
        }
    }

    /// <summary>Audibility score of a tile: lowpass cutoff times linear volume fraction, 0..1.</summary>
    private float Audibility(float sumOcclusion, int pathLen, int past, Vector2i tile, Vector2i seed)
    {
        var pastDb = sumOcclusion > 0f ? past : 0;
        var cutoff = MathF.Exp(-MathF.Max(0f, sumOcclusion - _system.OcclusionDecayPerTile * pastDb));
        var manhattan = Math.Abs(tile.X - seed.X) + Math.Abs(tile.Y - seed.Y);
        var lossDb = _system.LossDbPerTile * (MathF.Max(0f, pathLen - manhattan) + pastDb);
        return Math.Clamp(cutoff * MathF.Pow(10f, -lossDb / 20f), 0f, 1f);
    }

    /// <summary>Bucket color for an audibility score.</summary>
    private static Color AudibilityColor(float s)
    {
        if (s >= 0.7f)
            return new Color(0.2f, 1f, 0.2f);

        if (s >= 0.3f)
            return new Color(1f, 0.9f, 0.1f);

        return new Color(1f, 0.15f, 0.1f);
    }

    private void DrawWorld(in OverlayDrawArgs args)
    {
        var mode = _system.OverlayMode;
        if (mode == PathSoundOverlayMode.Off || !_system.FieldReady)
            return;

        if (_player.LocalEntity is not { } player ||
            !_entManager.TryGetComponent(player, out TransformComponent? playerXform))
            return;

        var gridUid = _system.FieldGrid;
        if (gridUid is not { } uid ||
            playerXform.GridUid != uid ||
            playerXform.MapID != args.MapId)
            return;

        if (!_entManager.TryGetComponent(uid, out MapGridComponent? gridComp))
            return;

        var worldHandle = args.WorldHandle;
        var seed = _system.FieldSeed;
        var radius = Math.Max(_system.Radius, 1);
        var tileSize = gridComp.TileSize;

        worldHandle.SetTransform(_transform.GetWorldMatrix(uid));

        var showTiles = mode is PathSoundOverlayMode.Field or PathSoundOverlayMode.All;
        if (showTiles)
        {
            var inv = _transform.GetInvWorldMatrix(uid);
            var aabb = args.WorldAABB;
            var cornerL = Vector2.Transform(aabb.BottomLeft, inv) / tileSize;
            var cornerR = Vector2.Transform(aabb.TopRight, inv) / tileSize;
            var minTile = new Vector2i(
                Math.Max((int) MathF.Floor(MathF.Min(cornerL.X, cornerR.X)) - 1, seed.X - radius),
                Math.Max((int) MathF.Floor(MathF.Min(cornerL.Y, cornerR.Y)) - 1, seed.Y - radius));
            var maxTile = new Vector2i(
                Math.Min((int) MathF.Ceiling(MathF.Max(cornerL.X, cornerR.X)) + 1, seed.X + radius),
                Math.Min((int) MathF.Ceiling(MathF.Max(cornerL.Y, cornerR.Y)) + 1, seed.Y + radius));

            for (var x = minTile.X; x <= maxTile.X; x++)
            {
                for (var y = minTile.Y; y <= maxTile.Y; y++)
                {
                    var indices = new Vector2i(x, y);
                    var box = new Box2(indices * tileSize, (indices + Vector2i.One) * tileSize);

                    if (indices == seed)
                    {
                        worldHandle.DrawRect(box, Color.White.WithAlpha(0.6f));
                        worldHandle.DrawRect(box, Color.White, filled: false);
                        continue;
                    }

                    if (_system.TryGetTile(indices, out var sumWeight, out var pathLen, out var past))
                    {
                        var s = Audibility(sumWeight, pathLen, past, indices, seed);
                        worldHandle.DrawRect(box, AudibilityColor(s).WithAlpha(0.22f + 0.5f * s));
                    }
                    else if (_mapSystem.TryGetTile(gridComp, indices, out var tile) && !tile.IsEmpty)
                    {
                        worldHandle.DrawRect(box, Color.DarkGray.WithAlpha(0.25f));
                    }
                }
            }
        }

        foreach (var (srcUid, entry) in _system.DebugEntries)
        {
            if (!_entManager.TryGetComponent(srcUid, out TransformComponent? srcXform) ||
                srcXform.MapID != args.MapId)
                continue;

            var pos = _transform.GetWorldPosition(srcXform);
            if (!args.WorldAABB.Enlarged(1f).Contains(pos))
                continue;

            var s = Math.Clamp(
                MathF.Exp(-entry.Occlusion) * MathF.Pow(10f, entry.VolumeDelta / 20f),
                0f,
                1f);
            var color = entry.Written ? AudibilityColor(s) : Color.LightGray;
            worldHandle.DrawCircle(pos, 0.45f, color.WithAlpha(0.9f), filled: false);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawScreen(in OverlayDrawArgs args)
    {
        var mode = _system.OverlayMode;
        if (mode == PathSoundOverlayMode.Off)
            return;

        var handle = args.ScreenHandle;
        var origin = new Vector2(args.ViewportBounds.Left + 8f, args.ViewportBounds.Top + 8f);

        DrawLegend(handle, origin);

        if (!_system.FieldReady || args.ViewportControl == null)
            return;

        if (_player.LocalEntity is not { } player ||
            !_entManager.TryGetComponent(player, out TransformComponent? playerXform))
            return;

        var gridUid = _system.FieldGrid;
        if (gridUid is not { } uid ||
            playerXform.GridUid != uid ||
            playerXform.MapID != args.MapId ||
            !_entManager.TryGetComponent(uid, out MapGridComponent? gridComp))
            return;

        var tileSize = gridComp.TileSize;
        var seed = _system.FieldSeed;

        if (mode is PathSoundOverlayMode.Numbers or PathSoundOverlayMode.All)
            DrawNumbers(args, uid, gridComp, seed, tileSize);

        DrawSourceLabels(args, uid);
    }

    private void DrawLegend(DrawingHandleScreen handle, Vector2 origin)
    {
        var rows = new (Color, string)[]
        {
            (new Color(0.2f, 1f, 0.2f), "clean (>=70%)"),
            (new Color(1f, 0.9f, 0.1f), "muffled (30-70%)"),
            (new Color(1f, 0.15f, 0.1f), "barely (<30%)"),
            (Color.DarkGray, "no path"),
        };

        var pos = origin;

        if (!_system.FieldReady)
        {
            handle.DrawString(_font, pos, "sound path: inactive", Color.Gray);
            return;
        }

        foreach (var (color, text) in rows)
        {
            handle.DrawRect(new UIBox2(pos, pos + new Vector2(12f, 12f)), color.WithAlpha(0.8f));
            handle.DrawString(_font, pos + new Vector2(16f, 1f), text, Color.White);
            pos += new Vector2(0f, 16f);
        }

        handle.DrawString(
            _font,
            pos + new Vector2(0f, 4f),
            $"wall occl x{_system.OcclusionPerWeight:0.0}  db/tile {_system.LossDbPerTile:0.0}  fade {_system.OcclusionDecayPerTile:0.0}/t  smooth {_system.Smoothing:0.00}  r {_system.Radius}",
            Color.LightGray);
    }

    private void DrawNumbers(in OverlayDrawArgs args, EntityUid gridUid, MapGridComponent grid, Vector2i seed, int tileSize)
    {
        var seedCenter = _mapTileCenterWorld(gridUid, grid, seed);
        var nextCenter = _mapTileCenterWorld(gridUid, grid, seed + Vector2i.One);
        var pxPerTile = (args.ViewportControl!.WorldToScreen(nextCenter) - args.ViewportControl.WorldToScreen(seedCenter)).Length();

        if (pxPerTile < 1f)
            return;

        var step = (int) MathF.Ceiling(18f / pxPerTile);
        if (step < 1)
            step = 1;

        var inv = _transform.GetInvWorldMatrix(gridUid);
        var aabb = args.WorldAABB;
        var radius = Math.Max(_system.Radius, 1);
        var cornerL = Vector2.Transform(aabb.BottomLeft, inv) / tileSize;
        var cornerR = Vector2.Transform(aabb.TopRight, inv) / tileSize;
        var minTile = new Vector2i(
            Math.Max((int) MathF.Floor(MathF.Min(cornerL.X, cornerR.X)), seed.X - radius),
            Math.Max((int) MathF.Floor(MathF.Min(cornerL.Y, cornerR.Y)), seed.Y - radius));
        var maxTile = new Vector2i(
            Math.Min((int) MathF.Ceiling(MathF.Max(cornerL.X, cornerR.X)), seed.X + radius),
            Math.Min((int) MathF.Ceiling(MathF.Max(cornerL.Y, cornerR.Y)), seed.Y + radius));

        var handle = args.ScreenHandle;

        for (var x = minTile.X; x <= maxTile.X; x += step)
        {
            for (var y = minTile.Y; y <= maxTile.Y; y += step)
            {
                var indices = new Vector2i(x, y);
                if (!_system.TryGetTile(indices, out var sumWeight, out var pathLen, out var past))
                    continue;

                var s = Audibility(sumWeight, pathLen, past, indices, seed);
                var center = _mapTileCenterWorld(gridUid, grid, indices);
                var screen = args.ViewportControl.WorldToScreen(center);
                handle.DrawString(_font, screen, $"{s * 100f:0}%", AudibilityColor(s).WithAlpha(0.95f));
            }
        }
    }

    private void DrawSourceLabels(in OverlayDrawArgs args, EntityUid gridUid)
    {
        if (!_entManager.TryGetComponent(gridUid, out TransformComponent? gridXform) ||
            gridXform.MapID != args.MapId)
            return;

        var handle = args.ScreenHandle;

        foreach (var (srcUid, entry) in _system.DebugEntries)
        {
            if (!_entManager.TryGetComponent(srcUid, out TransformComponent? srcXform) ||
                srcXform.MapID != args.MapId)
                continue;

            var pos = _transform.GetWorldPosition(srcXform);
            if (!args.WorldAABB.Enlarged(1f).Contains(pos))
                continue;

            var screen = args.ViewportControl!.WorldToScreen(pos);
            var color = entry.Written ? Color.White : Color.LightGray;
            handle.DrawString(_font, screen + new Vector2(10f, -6f), $"o:{entry.Occlusion:0.00} v:{entry.VolumeDelta:+0.0;-0.0}dB", color.WithAlpha(0.9f));
        }
    }

    private Vector2 _mapTileCenterWorld(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        return _mapSystem.GridTileToWorldPos(gridUid, grid, indices);
    }
}
