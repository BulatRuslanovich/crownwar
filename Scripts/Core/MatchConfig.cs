using System.Collections.Generic;
using System.Linq;
using Lordsio.Sim.AI;
using Lordsio.Sim.Core;

namespace Lordsio.Core;

public enum SlotKind
{
    Human,
    Bot,
}

/// <summary>One faction of the match: who controls it and which team it plays for.</summary>
public sealed record SlotConfig(SlotKind Kind, string Name, BotDifficulty Difficulty, int Team);

/// <summary>
/// How a match is set up: slot i becomes faction i. <paramref name="LocalFactionId"/> is the slot this
/// machine controls. A null seed means a new random layout each match. A <paramref name="Showcase"/> match
/// is the living backdrop of the menus: bots only, no interface, no fog, a drifting camera.
/// In <see cref="MatchMode.Defense"/> all slots defend one citadel together against waves; teams are ignored.
/// </summary>
public sealed record MatchConfig(IReadOnlyList<SlotConfig> Slots, int? Seed, int LocalFactionId = 0, bool Showcase = false,
    MatchMode Mode = MatchMode.Versus)
{
    /// <summary>Four bots fighting it out behind the main menu.</summary>
    public static MatchConfig ShowcaseMatch(int seed) => new(new[]
    {
        new SlotConfig(SlotKind.Bot, "", BotDifficulty.Hard, 0),
        new SlotConfig(SlotKind.Bot, "", BotDifficulty.Normal, 1),
        new SlotConfig(SlotKind.Bot, "", BotDifficulty.Hard, 2),
        new SlotConfig(SlotKind.Bot, "", BotDifficulty.Normal, 3),
    }, seed, Showcase: true);

    public static readonly MatchConfig Default = Single(3, BotDifficulty.Normal, null);

    /// <summary>Single player: you against <paramref name="botCount"/> bots, everyone for themselves.</summary>
    public static MatchConfig Single(int botCount, BotDifficulty difficulty, int? seed)
    {
        var slots = new List<SlotConfig> { new(SlotKind.Human, "", difficulty, 0) };
        for (int i = 1; i <= botCount; i++)
            slots.Add(new SlotConfig(SlotKind.Bot, "", difficulty, i));
        return new MatchConfig(slots, seed);
    }

    public int BotCount => Slots.Count(s => s.Kind == SlotKind.Bot);

    /// <summary>Difficulty of the first bot (the single-player menu sets all of them alike).</summary>
    public BotDifficulty Difficulty => Slots.FirstOrDefault(s => s.Kind == SlotKind.Bot)?.Difficulty ?? BotDifficulty.Normal;

    public IReadOnlyList<int> Teams => Slots.Select(s => s.Team).ToList();

    public bool IsDefense => Mode == MatchMode.Defense;
}
