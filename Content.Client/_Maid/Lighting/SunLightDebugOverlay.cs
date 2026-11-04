using System.Numerics;
using Content.Client.Resources;
using Content.Shared.Light.Components;
using Content.Shared._Maid.StationSunShadow;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Prints numeric sunlight / roof / shadow values for the map, the player's tile and the tile under the cursor.
/// Toggled with the <c>sunlightdebug</c> command.
/// </summary>
public sealed class SunLightDebugOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;
    private readonly Font _font;

    private readonly List<string> _lines = new();

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public SunLightDebugOverlay()
    {
        IoCManager.InjectDependencies(this);
        _mapSystem = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _font = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", 12);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _lines.Clear();
        _lines.Add("== Sun light debug ==");

        var ambient = 0f;
        if (_entManager.TryGetComponent(args.MapUid, out MapLightComponent? mapLight))
        {
            var c = mapLight.AmbientLightColor;
            ambient = Luminance(c);
            _lines.Add($"Map ambient: R{c.R:0.00} G{c.G:0.00} B{c.B:0.00}  lum={ambient:0.000}");
        }
        else
        {
            _lines.Add("Map ambient: none (black), lum=0.000");
        }

        if (_player.LocalEntity is { } player)
            DescribeTile("Player tile", _xform.GetMapCoordinates(player), ambient);

        var cursor = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);
        if (cursor.MapId == args.MapId)
            DescribeTile("Cursor tile", cursor, ambient);

        if (StationSunShadowOverlay.DebugInfo.Length > 0)
            _lines.Add(StationSunShadowOverlay.DebugInfo);

        var handle = args.ScreenHandle;
        var pos = new Vector2(10f, 120f);
        foreach (var line in _lines)
        {
            handle.DrawString(_font, pos, line, Color.Yellow);
            pos.Y += 14f;
        }
    }

    private void DescribeTile(string label, MapCoordinates coords, float ambient)
    {
        if (!_mapSystem.TryFindGridAt(coords, out var gridUid, out var grid))
        {
            _lines.Add($"{label}: open space, ambient lum={ambient:0.000}");
            return;
        }

        var index = _mapSystem.TileIndicesFor(gridUid, grid, coords);
        var rooved = false;
        var effectiveScale = 1f;
        var roofMode = "none";

        if (_entManager.HasComponent<StationSunMarkerComponent>(gridUid) &&
            _overlay.TryGetOverlay<StationSunShadowOverlay>(out var sunOverlay))
        {
            var light = sunOverlay.GetLight(gridUid, index);
            roofMode = $"station: sunlight through window {light:0.00}";
            effectiveScale = light;
            _lines.Add($"  tile state: {sunOverlay.Describe(gridUid, index)}");
        }
        else if (_entManager.HasComponent<ImplicitRoofComponent>(gridUid))
        {
            rooved = true;
            roofMode = "implicit (whole grid)";
        }

        var effective = rooved ? 0f : ambient * effectiveScale;
        _lines.Add($"{label} {index}: roof={roofMode}, ambient lum={effective:0.000}");

        if (_entManager.TryGetComponent(gridUid, out SunShadowComponent? shadow))
            _lines.Add($"  grid shadow: dir=({shadow.Direction.X:0.0},{shadow.Direction.Y:0.0}) alpha={shadow.Alpha:0.00}");
    }

    private static float Luminance(Color c)
    {
        return 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
    }
}
