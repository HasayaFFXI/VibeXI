namespace Zerg.Core;

/// <summary>
/// One damage row from the event file: one (action, target, result).
///
/// Times are milliseconds. The file says seconds, and <see cref="EventReader"/>
/// scales them once; nothing downstream scales again. Once
/// <see cref="Counting.Filter"/> has put a row on a session clock, <see cref="T"/>
/// is milliseconds since the session's zero and <see cref="Wall"/> keeps the
/// time of day it happened.
///
/// Numbers stay doubles, as the file's JSON numbers are, so nothing read is
/// rounded on the way in.
///
/// Immutable on purpose: every step that changes a row (crediting a pet,
/// moving it onto the session clock, folding a use) makes a copy with
/// <c>with</c>, so the list read from the file stays true to the file.
/// </summary>
public sealed record CombatEvent
{
    public double T { get; init; }
    /// <summary>Orders rows inside one second; not a clock.</summary>
    public double Seq { get; init; }
    /// <summary>One use of an action, shared by every row it produced (an AoE's
    /// targets); null on rows made by hand.</summary>
    public double? Use { get; init; }
    public string Kind { get; init; } = "";
    public string Actor { get; init; } = "";
    public string ActorKind { get; init; } = "";
    public string Action { get; init; } = "";
    public double ActionId { get; init; }
    public string Target { get; init; } = "";
    public string TargetKind { get; init; } = "";
    public double Dmg { get; init; }
    public bool Hit { get; init; }
    public bool Crit { get; init; }
    public bool Burst { get; init; }
    /// <summary>The game's own message id for the result.</summary>
    public double Msg { get; init; }
    /// <summary>Which record of the file (1-based) this came from.</summary>
    public int Line { get; init; }

    /// <summary>On a pet's own rows only: its master.</summary>
    public string? Owner { get; init; }
    /// <summary>On a pet's own rows only: the pet's name.</summary>
    public string? Pet { get; init; }
    /// <summary>Who actually acted, once a pet's row has been credited to its
    /// owner (<see cref="Counting.Credit"/>).</summary>
    public string? By { get; init; }
    /// <summary>The time of day the row happened, once it is on a session clock.</summary>
    public double? Wall { get; init; }

    /// <summary>On a folded use (<see cref="Counting.Collapse"/>): every target
    /// it reached, in order.</summary>
    public IReadOnlyList<string>? Targets { get; init; }
    /// <summary>On a folded use: how many rows it was made of.</summary>
    public int? Parts { get; init; }

    /// <summary>A pet's row (the owner field is set, and not empty).</summary>
    public bool IsPets => !string.IsNullOrEmpty(Owner);
}

/// <summary>
/// One heal (one target of one healing action), kept apart from the damage
/// rows so no damage figure can ever count it. <see cref="Hp"/> is the amount.
/// </summary>
public sealed record HealEvent
{
    public double T { get; init; }
    public double Seq { get; init; }
    public double? Use { get; init; }
    /// <summary>Which kind of heal this is: <c>magic</c>, <c>ability</c> or <c>pet</c>.</summary>
    public string Via { get; init; } = "";
    public string Actor { get; init; } = "";
    public string ActorKind { get; init; } = "";
    public string Action { get; init; } = "";
    public double ActionId { get; init; }
    public string Target { get; init; } = "";
    public string TargetKind { get; init; } = "";
    public double Hp { get; init; }
    public double Msg { get; init; }
    public int Line { get; init; }
    public string? Owner { get; init; }
    public string? Pet { get; init; }
    public string? By { get; init; }
    public double? Wall { get; init; }

    public bool IsPets => !string.IsNullOrEmpty(Owner);
}

/// <summary>A party member's jobs, from the addon's job lines. <c>NON</c> is
/// the game's "none".</summary>
public sealed record JobInfo(string Main, double MainId, double MainLevel,
                             string Sub, double SubId, double SubLevel);
