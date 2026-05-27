namespace RangedrifterClone.Components;

public class AIComponent
{
    public AIBehavior Behavior { get; set; } = AIBehavior.BasicMonster;
    public bool IsAware { get; set; } = false;
    public int TurnsUnaware { get; set; } = 0;
}

public enum AIBehavior { BasicMonster, Passive, Aggressive }
