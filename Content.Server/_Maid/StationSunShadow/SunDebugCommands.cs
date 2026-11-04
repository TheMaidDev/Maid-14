using System.Globalization;
using System.Numerics;
using Content.Shared.Administration;
using Content.Shared.Light.Components;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Shared argument handling for the sunlight debug commands.
/// </summary>
internal static class SunCommandHelpers
{
    public static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Grid from the argument at <paramref name="index"/> if present, otherwise the grid the caller stands on.
    /// </summary>
    public static bool TryGetGrid(IEntityManager entMan, IConsoleShell shell, string[] args, int index, out EntityUid grid)
    {
        grid = default;

        if (args.Length > index)
        {
            if (!NetEntity.TryParse(args[index], out var net) ||
                !entMan.TryGetEntity(net, out var uid) ||
                !entMan.HasComponent<MapGridComponent>(uid.Value))
            {
                shell.WriteError($"'{args[index]}' is not a grid entity.");
                return false;
            }

            grid = uid.Value;
            return true;
        }

        if (shell.Player?.AttachedEntity is { } attached &&
            entMan.GetComponent<TransformComponent>(attached).GridUid is { } below)
        {
            grid = below;
            return true;
        }

        shell.WriteError("Stand on a grid or pass a grid entity id.");
        return false;
    }
}

/// <summary>
/// <c>sunspin [degrees per second] [grid]</c>: starts rotating a grid, 0 stops it.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class SunSpinCommand : LocalizedEntityCommands
{
    public override string Command => "sunspin";
    public override string Description => "Rotates a grid around its centre. 0 stops it.";
    public override string Help => "sunspin <degrees per second> [grid entity id] (defaults to the grid you stand on)";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !SunCommandHelpers.TryParseFloat(args[0], out var speed))
        {
            shell.WriteLine(Help);
            return;
        }

        if (!SunCommandHelpers.TryGetGrid(EntityManager, shell, args, 1, out var grid))
            return;

        if (speed == 0f)
        {
            EntityManager.RemoveComponent<GridSpinComponent>(grid);
            shell.WriteLine($"Grid {grid} stopped.");
            return;
        }

        var spin = EntityManager.EnsureComponent<GridSpinComponent>(grid);
        spin.DegreesPerSecond = speed;
        shell.WriteLine($"Grid {grid} spins at {speed.ToString(CultureInfo.InvariantCulture)} deg/s.");
    }
}

/// <summary>
/// <c>sunset &lt;angle&gt; [length] [alpha] [grid]</c>: sets the direction of the sun on a grid.
/// The angle is the side the light comes from: 0 = north, clockwise (90 = east, 180 = south).
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class SunSetCommand : LocalizedEntityCommands
{
    public override string Command => "sunset";
    public override string Description => "Sets the sun direction (angle the light comes from, 0 = north, clockwise).";
    public override string Help => "sunset <angle> [length] [alpha] [grid entity id]; use - to keep a value";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !SunCommandHelpers.TryParseFloat(args[0], out var angle))
        {
            shell.WriteLine(Help);
            return;
        }

        if (!SunCommandHelpers.TryGetGrid(EntityManager, shell, args, 3, out var grid))
            return;

        var sun = EntityManager.EnsureComponent<SunShadowComponent>(grid);

        var length = sun.Direction.Length();
        if (args.Length > 1 && args[1] != "-")
        {
            if (!SunCommandHelpers.TryParseFloat(args[1], out length) || length <= 0f)
            {
                shell.WriteError("Length must be a positive number.");
                return;
            }
        }
        else if (length <= 0f)
        {
            length = 40f;
        }

        if (args.Length > 2 && args[2] != "-")
        {
            if (!SunCommandHelpers.TryParseFloat(args[2], out var alpha) || alpha < 0f || alpha > 1f)
            {
                shell.WriteError("Alpha must be between 0 and 1.");
                return;
            }

            sun.Alpha = alpha;
        }

        // The light travels away from the side it comes from; world +Y is north.
        var radians = MathF.PI / 180f * angle;
        sun.Direction = new Vector2(-MathF.Sin(radians), -MathF.Cos(radians)) * length;
        EntityManager.Dirty(grid, sun);

        shell.WriteLine($"Grid {grid}: light from {angle.ToString("0.#", CultureInfo.InvariantCulture)} deg, length {length.ToString("0.#", CultureInfo.InvariantCulture)}, alpha {sun.Alpha.ToString("0.##", CultureInfo.InvariantCulture)}.");
    }
}
