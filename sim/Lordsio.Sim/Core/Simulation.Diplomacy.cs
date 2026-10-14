namespace Lordsio.Sim.Core;

/// <summary>How two factions stand towards each other.</summary>
public enum Stance
{
    /// <summary>The same team from the lobby: allies for good, who share sight and win together.</summary>
    Ally,
    /// <summary>A treaty: neither side's troops, towers or stones can harm the other's.</summary>
    Peace,
    War,
}

/// <summary>
/// Diplomacy between factions of different teams. Everyone starts at war. Peace takes both: one side offers it, the
/// other accepts within the offer's time (an offer back to someone who has offered counts as accepting). War is
/// declared by one side alone but begins only after a warning, so neither can strike at troops standing trustingly
/// in its lands. Peace does not end a match: only one team can win.
/// </summary>
public partial class Simulation
{
    /// <summary>Treaties in force, by the pair of faction ids (kept symmetric).</summary>
    private bool[,] _peace = new bool[0, 0];
    /// <summary>Tick an offer of peace from the first faction to the second runs out; 0 for none.</summary>
    private int[,] _offerExpires = new int[0, 0];
    /// <summary>Tick the war the first faction declared on the second begins; 0 for none.</summary>
    private int[,] _warStarts = new int[0, 0];

    private void InitDiplomacy(int factionSlots)
    {
        _peace = new bool[factionSlots, factionSlots];
        _offerExpires = new int[factionSlots, factionSlots];
        _warStarts = new int[factionSlots, factionSlots];
    }

    /// <summary>How the two factions stand; neutrals and unknown ids are at war with everyone.</summary>
    public Stance StanceBetween(int factionA, int factionB)
    {
        if (factionA == factionB)
            return Stance.Ally;
        if (!IsFactionSlot(factionA) || !IsFactionSlot(factionB))
            return Stance.War;
        if (_teamOf[factionA] == _teamOf[factionB])
            return Stance.Ally;
        return _peace[factionA, factionB] ? Stance.Peace : Stance.War;
    }

    /// <summary>Whether <paramref name="from"/> has offered peace to <paramref name="to"/> and the offer still stands.</summary>
    public bool HasPeaceOffer(int from, int to) => IsFactionSlot(from) && IsFactionSlot(to) && _offerExpires[from, to] > Tick;

    /// <summary>Seconds left to accept the offer of peace from <paramref name="from"/> to <paramref name="to"/>; 0 if none.</summary>
    public float PeaceOfferSecondsLeft(int from, int to) =>
        HasPeaceOffer(from, to) ? (_offerExpires[from, to] - Tick) / (float)TicksPerSecond : 0f;

    /// <summary>Seconds until a declared war between the two begins; null when none is coming.</summary>
    public float? SecondsToWar(int factionA, int factionB)
    {
        if (!IsFactionSlot(factionA) || !IsFactionSlot(factionB))
            return null;
        int starts = Math.Max(_warStarts[factionA, factionB], _warStarts[factionB, factionA]);
        return starts > Tick ? (starts - Tick) / (float)TicksPerSecond : null;
    }

    private bool IsFactionSlot(int factionId) => factionId >= 0 && factionId < _teamOf.Length;

    private void ApplyProposePeace(ProposePeaceCommand offer)
    {
        int from = offer.FactionId, to = offer.OtherFactionId;
        if (Check(from, to, wantPeace: true) is { } error)
        {
            _events.Add(new DiplomacyRejected(from, to, error));
            return;
        }
        if (HasPeaceOffer(to, from))
        {
            MakePeace(to, from); // they had already offered: both want it
            return;
        }
        if (HasPeaceOffer(from, to))
        {
            _events.Add(new DiplomacyRejected(from, to, DiplomacyError.AlreadyOffered));
            return;
        }
        _offerExpires[from, to] = Tick + SecondsToTicks(Rules.PeaceOfferSeconds);
        _events.Add(new PeaceOffered(from, to, Rules.PeaceOfferSeconds));
    }

    private void ApplyAnswerPeace(AnswerPeaceCommand answer)
    {
        int me = answer.FactionId, proposer = answer.OtherFactionId;
        if (!HasPeaceOffer(proposer, me))
        {
            _events.Add(new DiplomacyRejected(me, proposer, DiplomacyError.NoOffer));
            return;
        }
        if (answer.Accept)
        {
            if (Check(me, proposer, wantPeace: true) is { } error)
            {
                _events.Add(new DiplomacyRejected(me, proposer, error));
                return;
            }
            MakePeace(proposer, me);
            return;
        }
        _offerExpires[proposer, me] = 0;
        _events.Add(new PeaceRefused(me, proposer, Expired: false));
    }

    private void ApplyDeclareWar(DeclareWarCommand declaration)
    {
        int from = declaration.FactionId, to = declaration.OtherFactionId;
        if (Check(from, to, wantPeace: false) is { } error)
        {
            _events.Add(new DiplomacyRejected(from, to, error));
            return;
        }
        if (SecondsToWar(from, to) != null)
        {
            _events.Add(new DiplomacyRejected(from, to, DiplomacyError.WarComing));
            return;
        }
        int starts = Tick + SecondsToTicks(Rules.WarWarningSeconds);
        _warStarts[from, to] = starts;
        _events.Add(new WarDeclared(from, to, Rules.WarWarningSeconds));
        if (starts <= Tick)
            BeginWar(from, to);
    }

    /// <summary>Why the two cannot change their relations that way, or null if they can.</summary>
    private DiplomacyError? Check(int from, int to, bool wantPeace)
    {
        if (from == to)
            return DiplomacyError.Self;
        if (!IsFactionSlot(from) || !IsFactionSlot(to) || GetFaction(to) is not { IsContender: true, IsEliminated: false })
            return DiplomacyError.UnknownFaction;
        return StanceBetween(from, to) switch
        {
            Stance.Ally => DiplomacyError.Allies,
            Stance.Peace when wantPeace => DiplomacyError.AlreadyAtPeace,
            Stance.War when !wantPeace => DiplomacyError.AlreadyAtWar,
            _ => null,
        };
    }

    private void MakePeace(int proposer, int accepter)
    {
        _offerExpires[proposer, accepter] = _offerExpires[accepter, proposer] = 0;
        _peace[proposer, accepter] = _peace[accepter, proposer] = true;
        // Fights between them stop at once: nobody keeps chasing or shooting at a friend.
        foreach (var unit in _units)
        {
            if (unit.Target is not { } target || StanceBetween(unit.FactionId, target.FactionId) != Stance.Peace)
                continue;
            if (unit.Order == Lordsio.Sim.Units.UnitOrder.AttackUnit)
                unit.CompleteOrder();
            unit.Target = null;
        }
        foreach (var building in _buildings)
            if (building.Target is { } shotAt && StanceBetween(building.FactionId, shotAt.FactionId) == Stance.Peace)
                building.Target = null;
        _events.Add(new PeaceMade(proposer, accepter));
    }

    private void BeginWar(int declarer, int other)
    {
        _warStarts[declarer, other] = _warStarts[other, declarer] = 0;
        _peace[declarer, other] = _peace[other, declarer] = false;
        _events.Add(new WarBegan(declarer, other));
    }

    /// <summary>Offers run out and declared wars break out when their time comes.</summary>
    private void UpdateDiplomacy()
    {
        int n = _teamOf.Length;
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                if (_offerExpires[a, b] != 0 && _offerExpires[a, b] <= Tick)
                {
                    _offerExpires[a, b] = 0;
                    _events.Add(new PeaceRefused(b, a, Expired: true));
                }
                if (_warStarts[a, b] != 0 && _warStarts[a, b] <= Tick)
                    BeginWar(a, b);
            }
    }

    private static int SecondsToTicks(float seconds) => (int)MathF.Round(seconds * TicksPerSecond);

    private void HashDiplomacy(ref Fnv hash)
    {
        int n = _teamOf.Length;
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                hash.Add(_peace[a, b] ? 1 : 0);
                hash.Add(_offerExpires[a, b]);
                hash.Add(_warStarts[a, b]);
            }
    }
}
