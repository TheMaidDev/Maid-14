using Content.Server.Administration;
using Content.Server.Mind;
using Content.Shared.Administration;
using Robust.Shared.Player;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Errors;

namespace Content.Server._Maid.Toolshed.Commands;

[ToolshedCommand(Name = "mindutils"), AdminCommand(AdminFlags.Fun), AdminCommand(AdminFlags.Debug)]
public sealed class MindUtilsCommands : ToolshedCommand
{
    [CommandImplementation("controlmob")]
    public EntityUid ControlMob(
        IInvocationContext ctx,
        [PipedArgument] EntityUid target,
        ICommonSession player)
    {
        if (!EntityManager.EntityExists(target))
            return target;

        var mind = GetSys<MindSystem>();
        mind.ControlMob(player.UserId, target);
        return target;
    }

    [CommandImplementation("controlmobself")]
    public EntityUid ControlMobSelf(
        IInvocationContext ctx,
        [PipedArgument] EntityUid target)
    {
        if (ctx.Session is not { } player)
        {
            ctx.ReportError(new NotForServerConsoleError());
            return target;
        }

        return ControlMob(ctx, target, player);
    }
}
