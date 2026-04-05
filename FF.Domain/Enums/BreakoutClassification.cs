namespace FF.Domain.Enums;

public enum BreakoutClassification
{
    Breakout,    // strong upward signals across multiple dimensions
    OnCurve,     // performing as expected for age/role
    Declining,   // usage or efficiency trending down
    Unknown      // insufficient data
}