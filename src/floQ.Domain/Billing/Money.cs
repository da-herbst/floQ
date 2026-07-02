namespace floQ.Domain.Billing;

/// <summary>Geld-Rundung — Single Source of Truth (batOS-Konvention):
/// kaufmännisch auf 2 Nachkommastellen, weg von Null.</summary>
public static class Money
{
    public static decimal Round(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
