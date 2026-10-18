using Warcrown.Sim.Units;

namespace Warcrown.Sim.Core;

/// <summary>Doctrines: the faction's one-time choice of how to make war, and the units as it trains them.</summary>
public sealed partial class Simulation
{
    /// <summary>Every kind of unit as each doctrine trains it, made once (doctrine id → unit id → type).</summary>
    private Dictionary<string, Dictionary<string, UnitType>>? _doctrineTypes;

    /// <summary>
    /// The kind as the faction trains it: with its doctrine's numbers once it has chosen one, the rules' own before
    /// (and for the horde and other factions without doctrine).
    /// </summary>
    public UnitType UnitTypeFor(int factionId, string typeId)
    {
        if (GetFaction(factionId)?.Doctrine is not { } doctrine)
            return Rules.Units.Get(typeId);
        _doctrineTypes ??= Rules.Doctrines.ToDictionary(d => d.Id, d => Rules.Units.All.ToDictionary(t => t.Id, d.Apply));
        return _doctrineTypes[doctrine].TryGetValue(typeId, out var type) ? type : Rules.Units.Get(typeId);
    }

    /// <summary>Whether a <see cref="ChooseDoctrineCommand"/> would currently succeed.</summary>
    public DoctrineError CanChooseDoctrine(int factionId, string doctrineId)
    {
        if (Rules.FindDoctrine(doctrineId) == null)
            return DoctrineError.UnknownDoctrine;
        if (GetFaction(factionId) is not { IsEliminated: false } faction)
            return DoctrineError.NotOwner;
        if (faction.Doctrine != null)
            return DoctrineError.AlreadyChosen;
        if (UnlockLevel(factionId) < Rules.DoctrineBarracks)
            return DoctrineError.NeedsBarracks;
        return DoctrineError.None;
    }

    private void ApplyChooseDoctrine(ChooseDoctrineCommand choose)
    {
        var error = CanChooseDoctrine(choose.FactionId, choose.DoctrineId);
        if (error != DoctrineError.None)
        {
            _events.Add(new DoctrineRejected(choose.FactionId, choose.DoctrineId, error));
            return;
        }
        GetFaction(choose.FactionId)!.Doctrine = choose.DoctrineId;
        _events.Add(new DoctrineChosen(choose.FactionId, choose.DoctrineId));
    }

    private int IndexOfDoctrine(string id)
    {
        for (int i = 0; i < Rules.Doctrines.Count; i++)
        {
            if (Rules.Doctrines[i].Id == id)
                return i;
        }
        return -1;
    }
}
