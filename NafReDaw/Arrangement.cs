namespace NafReDaw;

public class Arrangement
{
    public float Bpm { get; set; } = 120f;

    public List<Pattern> Patterns { get; set; } = [new Pattern()];
}

public class Pattern
{
    public string Name { get; set; } = "Pattern 1";

    /// <summary>
    /// [track][step] → sample pad note to play, or <see cref="ArrangeSystem.EmptyStep"/> if unused.
    /// </summary>
    public int[][] Steps { get; set; } = [];
}
