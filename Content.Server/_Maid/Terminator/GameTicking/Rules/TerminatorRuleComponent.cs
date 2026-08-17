using Content.Shared.Whitelist;
using Robust.Shared.Audio;

namespace Content.Server._Maid.Terminator.GameTicking.Rules;

[RegisterComponent]
public sealed partial class TerminatorRuleComponent : Component
{
    [DataField]
    public SoundSpecifier? BriefingSound;

    [DataField]
    public EntityWhitelist? TargetBlacklist;

    [DataField]
    public EntityUid? Target;
}
