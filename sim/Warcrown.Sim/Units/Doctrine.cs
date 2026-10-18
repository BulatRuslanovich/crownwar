namespace Warcrown.Sim.Units;

/// <summary>
/// A way of making war a faction commits to once, when it has <see cref="Core.GameRules.DoctrineBarracks"/> barracks:
/// it opens the kinds of units marked with its id (<see cref="UnitType.Doctrine"/>) and changes every recruit trained
/// from then on — fast and cheap but frail, or slow and dear but strong. Factors multiply the unit's own numbers.
/// </summary>
public sealed record Doctrine(
    string Id,
    string Name,
    float SpeedFactor = 1f,
    float HealthFactor = 1f,
    float DamageFactor = 1f,
    float CostFactor = 1f,
    float UpkeepFactor = 1f,
    float BuildTimeFactor = 1f)
{
    /// <summary>The unit as this doctrine trains it: the same kind (id, counters, looks) with its numbers changed.</summary>
    public UnitType Apply(UnitType type) => type with
    {
        Speed = type.Speed * SpeedFactor,
        MaxHealth = Math.Max(1, Scale(type.MaxHealth, HealthFactor)),
        Damage = Scale(type.Damage, DamageFactor),
        Cost = (int)MathF.Round(type.Cost * CostFactor / 5f, MidpointRounding.AwayFromZero) * 5, // prices in fives
        Upkeep = Scale(type.Upkeep, UpkeepFactor),
        BuildTime = type.BuildTime * BuildTimeFactor,
    };

    private static int Scale(int value, float factor) => (int)MathF.Round(value * factor, MidpointRounding.AwayFromZero);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("A doctrine needs an id.");
        if (SpeedFactor <= 0f || HealthFactor <= 0f || DamageFactor < 0f || CostFactor <= 0f || UpkeepFactor < 0f || BuildTimeFactor <= 0f)
            throw new ArgumentException($"Doctrine '{Id}': speed, health, cost and training time factors must be positive, damage and upkeep not negative.");
    }
}
