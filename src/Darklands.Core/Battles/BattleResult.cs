namespace Darklands.Core.Battles;

/// <summary>How a battle ended (<c>battle-outcome</c>, DS:A893); <c>battle-entry</c> returns it to the cards.</summary>
// orig: fight-finished R014+0x08FE (0E76:08FE) draft; battle-entry R002+0x0000 (result texts)
public enum BattleResult
{
    /// <summary>−1: still fighting.</summary>
    Running = -1,

    /// <summary>0: the enemies are beaten ("victory").</summary>
    Won = 0,

    /// <summary>2: the party fled through an exit ("retreat"); also what 8 becomes for the cards.</summary>
    Fled = 2,

    /// <summary>3: the party lost but somebody got away ("defeat").</summary>
    Lost = 3,

    /// <summary>4: the party surrendered.</summary>
    Surrendered = 4,

    /// <summary>5: everybody is down ("all dead"): the game ends.</summary>
    AllDown = 5,

    /// <summary>6: the party fell into a pit ("Aaaahh!", the pit trigger R014 19CD:2312); no skill improvement, no results box.</summary>
    FellIntoPit = 6,

    /// <summary>7: a timed exit.</summary>
    TimedExit = 7,

    /// <summary>8: left the field before any fight ("leave"); battle-entry reports it as 2.</summary>
    LeftBeforeFight = 8,
}
