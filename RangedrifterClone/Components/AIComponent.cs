namespace RangedrifterClone.Components;

/// <summary>
/// Full AI descriptor: behavior archetype + state machine + memory.
/// Replaces the earlier simple IsAware bool with a proper state enum and
/// last-known-position memory so enemies intelligently investigate.
/// </summary>
public class AIComponent
{
    // ── Archetype ──────────────────────────────────────────────────────────
    public AIBehavior Behavior      { get; set; } = AIBehavior.BasicMonster;
    public bool       IsRanged      { get; set; } = false;   // uses ranged attack
    public int        AttackRange   { get; set; } = 1;
    public int        AlertRadius   { get; set; } = 6;        // tiles to hear noise
    public string     PackTag       { get; set; } = "";       // same tag = pack coordination
    public bool       CanFlee       { get; set; } = false;
    public float      FleeThreshold { get; set; } = 0.20f;    // flee at 20% HP

    // ── State Machine ──────────────────────────────────────────────────────
    public AIState State { get; set; } = AIState.Idle;
    public int     StateTimer { get; set; } = 0;             // turns spent in current state

    // ── Memory ────────────────────────────────────────────────────────────
    /// <summary>Last map position the AI saw the player.</summary>
    public SadRogue.Primitives.Point? LastKnownPlayerPos { get; set; }
    /// <summary>Turns since the player was last in FOV of this AI.</summary>
    public int TurnsSincePlayerSeen { get; set; } = 0;
    /// <summary>Legacy: kept for compatibility, mapped to State != Idle.</summary>
    public bool IsAware
    {
        get => State != AIState.Idle;
        set { if (value && State == AIState.Idle) State = AIState.Alerted; }
    }
}

public enum AIBehavior
{
    BasicMonster,  // standard melee chaser
    Passive,       // never attacks
    Aggressive,    // always hostile, never flees
    Ranged,        // stays at range, fires projectiles
    Coward,        // flees when low HP
    Pack,          // alerts nearby pack-mates when it spots the player
    Boss,          // enhanced, multiple attacks
}

public enum AIState
{
    Idle,           // wandering or stationary; hasn't seen player
    Alerted,        // saw/heard something suspicious; moves toward noise
    Hunting,        // actively chasing the player (player in memory)
    Investigating,  // player left FOV; moving toward LastKnownPlayerPos
    Fleeing,        // running away (low HP or Coward type)
}
