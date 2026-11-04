using System.Globalization;
using System.Numerics;
using Content.Client.Administration.Managers;
using Content.Shared.Light.Components;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Admin window that controls and visualises the station sunlight. Opened from the admin panel (B) or with <c>sunlight</c>.
/// </summary>
public sealed class SunDebugWindow : DefaultWindow
{
    private static SunDebugWindow? _instance;

    [Dependency] private readonly IClientConsoleHost _console = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly LineEdit _angle = new() { Text = "0", MinWidth = 60 };
    private readonly LineEdit _length = new() { Text = "40", MinWidth = 60 };
    private readonly LineEdit _alpha = new() { Text = "1", MinWidth = 60 };
    private readonly LineEdit _spin = new() { Text = "10", MinWidth = 60 };
    private readonly Label _status = new() { Text = "..." };
    private float _statusTimer;

    private SunDebugWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = "Sun debug";
        MinSize = new Vector2(380, 0);
        OnClose += () =>
        {
            if (_instance == this)
                _instance = null;
        };

        var root = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(6) };
        Contents.AddChild(root);

        root.AddChild(_status);

        // Direction presets: the angle is the side the light comes from, 0 = north, clockwise.
        root.AddChild(new Label { Text = "Light comes from (deg, 0 = north, clockwise):" });
        var presets = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        foreach (var (name, deg) in new[] { ("N", 0), ("NE", 45), ("E", 90), ("SE", 135), ("S", 180), ("SW", 225), ("W", 270), ("NW", 315) })
        {
            var button = new Button { Text = name, HorizontalExpand = true };
            var angle = deg;
            button.OnPressed += _ =>
            {
                _angle.Text = angle.ToString(CultureInfo.InvariantCulture);
                Apply();
            };
            presets.AddChild(button);
        }

        root.AddChild(presets);

        var fields = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        fields.AddChild(new Label { Text = "angle" });
        fields.AddChild(_angle);
        fields.AddChild(new Label { Text = "length" });
        fields.AddChild(_length);
        fields.AddChild(new Label { Text = "alpha" });
        fields.AddChild(_alpha);
        root.AddChild(fields);

        var apply = new Button { Text = "Apply (angle, length, alpha)" };
        apply.OnPressed += _ => Apply();
        root.AddChild(apply);

        // Grid rotation, for testing on real stations.
        root.AddChild(new Label { Text = "Rotate the grid you stand on (deg/s):" });
        var spinRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        spinRow.AddChild(_spin);
        var start = new Button { Text = "Start", HorizontalExpand = true };
        start.OnPressed += _ => Run("sunspin", Number(_spin, "10"));
        var stop = new Button { Text = "Stop", HorizontalExpand = true };
        stop.OnPressed += _ => Run("sunspin", "0");
        spinRow.AddChild(start);
        spinRow.AddChild(stop);
        root.AddChild(spinRow);

        // Layers.
        root.AddChild(new Label { Text = "Layers:" });
        root.AddChild(Layer("Light arrows and open-to-sun tiles", () => new SunLightRaysOverlay()));
        root.AddChild(Layer("Numbers (ambient, roof, shadow info)", () => new SunLightDebugOverlay()));
    }

    /// <summary>
    /// Opens the window, or closes it if it is already open.
    /// </summary>
    public static void Toggle()
    {
        if (_instance != null)
        {
            _instance.Close();
            return;
        }

        _instance = new SunDebugWindow();
        _instance.OpenCentered();
    }

    private static string Number(LineEdit edit, string fallback)
    {
        var text = edit.Text.Trim().Replace(',', '.');
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? text : fallback;
    }

    private void Run(string command, string args)
    {
        _console.ExecuteCommand($"{command} {args}");
    }

    private void Apply()
    {
        Run("sunset", $"{Number(_angle, "0")} {Number(_length, "40")} {Number(_alpha, "1")}");
    }

    private CheckBox Layer<T>(string text, Func<T> create) where T : Overlay
    {
        var box = new CheckBox { Text = text, Pressed = _overlay.HasOverlay<T>() };
        box.OnToggled += args =>
        {
            if (args.Pressed && !_overlay.HasOverlay<T>())
                _overlay.AddOverlay(create());
            else if (!args.Pressed && _overlay.HasOverlay<T>())
                _overlay.RemoveOverlay<T>();
        };
        return box;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _statusTimer -= args.DeltaSeconds;
        if (_statusTimer > 0f)
            return;

        _statusTimer = 0.5f;

        if (_player.LocalEntity is not { } player ||
            _entManager.GetComponent<TransformComponent>(player).GridUid is not { } grid ||
            !_entManager.TryGetComponent(grid, out SunShadowComponent? sun))
        {
            _status.Text = "You are not on a grid with SunShadow.";
            return;
        }

        var dir = sun.Direction;
        var from = -dir;
        var angle = MathF.Atan2(from.X, from.Y) * 180f / MathF.PI;
        if (angle < 0f)
            angle += 360f;

        var theta = _entManager.System<SharedTransformSystem>().GetWorldRotation(grid).Degrees;
        _status.Text = $"Grid {grid}: light from {angle:0}°, length {dir.Length():0.#}, alpha {sun.Alpha:0.##}, grid rotation {theta:0.#}°";
    }
}

/// <summary>
/// Button for the admin panel that opens <see cref="SunDebugWindow"/>. Hidden for non-admins.
/// </summary>
public sealed class SunDebugButton : Button
{
    public SunDebugButton()
    {
        Text = "Sun debug";
        OnPressed += _ => SunDebugWindow.Toggle();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        Visible = IoCManager.Resolve<IClientAdminManager>().HasFlag(Content.Shared.Administration.AdminFlags.Debug);
    }
}
