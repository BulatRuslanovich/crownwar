using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>
/// A building ordered where none of the faction's troops stand: it is paid for at once, and the nearest free soldier
/// walks over; once someone is in building range the site is laid out like any other. If the builder falls, is sent
/// elsewhere or gets stuck, or the place is taken meanwhile, the order is dropped and the gold handed back.
/// </summary>
public sealed class BuildPlan
{
    internal BuildPlan(int id, int factionId, BuildingType type, Cell anchor, int builderId, int paid, Vector2? goal)
    {
        Id = id;
        FactionId = factionId;
        Type = type;
        Anchor = anchor;
        BuilderId = builderId;
        Paid = paid;
        Goal = goal;
    }

    public int Id { get; }
    public int FactionId { get; }
    public BuildingType Type { get; }
    public Cell Anchor { get; }
    /// <summary>The soldier walking over to build.</summary>
    public int BuilderId { get; }
    /// <summary>Gold already spent, returned if the order is dropped.</summary>
    public int Paid { get; }
    /// <summary>Where the builder was sent; a builder given another destination has been called off.</summary>
    internal Vector2? Goal { get; }

    public Vector2 Center => Building.CenterOf(Anchor, Type.Size);
}
