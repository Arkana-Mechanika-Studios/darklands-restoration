using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// The doors to the two realms deep in the mine ($MineR08 the dwarfs' golden door, $MineR09 the
/// kobolds' rusty iron door). The party may go through (line 0) or fight its way back (line 1,
/// battle −2). Behind the door the folk the party promised to help greet it (text 4, the
/// promise is used up) or, if it promised the other folk, attack it (text 3); both are fights
/// of −4 with combat flag 8. If nothing was promised, an envoy of the other folk speaks, the
/// party is counted on their side for a month, and the fight is −3.
/// </summary>
/// <remarks>
/// The card asks once, with no loop, and the option hints are only shown, never called on the
/// choice. Neither card resets the item and saint lists.
/// </remarks>
// orig: the shared shape of R103+0x0000 (1838:0000) and R103+0x0344 (186C:0004),
// matched (each card class cites its own).
public abstract class MineRealmDoor : Card
{
    protected const int MineQuestTag = 0x0C;

    /// <summary>The mine facts of the two promises made to the kobold envoy (MineR07): 6 help, 5 refusal.</summary>
    protected const int PromisedTheKobolds = 6;

    protected const int RefusedTheKobolds = 5;

    private const int Picture = 0;
    private const int DoorPicture = 213;

    /// <summary>The fact under which the folk behind this door greet the party (it is removed).</summary>
    protected abstract int Welcome { get; }

    /// <summary>The fact under which they attack it; set when nothing was promised.</summary>
    protected abstract int Enmity { get; }

    /// <summary>The picture of the folk behind the door.</summary>
    protected abstract int FolkPicture { get; }

    /// <summary>The two pictures of the envoy's speech (texts 1 and 2).</summary>
    protected abstract (int First, int Second) EnvoyPictures { get; }

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        c.Next = Number;
        var choice = 0;
        var menu = Menu(c, 5);
        menu.Enable(0, 1);
        // C: the original offers the menu once; the actions only note the choice. CardMenu.Run loops while
        // c.Next is the card's own number, so the actions leave it for a moment and it is put back below.
        menu.Run(
            new MenuLine(() => { choice = 0; c.Next = 0; }, OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(() => { choice = 1; c.Next = 0; }, OptionHint.Saying(CardTexts.Unknown)));
        c.Next = Number;
        if (choice != 0)
        {
            c.BattleMode = 1;
            return c.Next = -2;
        }
        if (c.Events.Fact(MineQuestTag, Welcome, c.Here))
        {
            c.Events.Remove(EventKinds.LocationFact, Welcome, c.Here, -1, MineQuestTag);
            ShowPicture(c, FolkPicture);
            c.Screen.Text(4);
            c.CombatFlags |= 8;
            c.BattleMode = 1;
            c.Next = -4;
        }
        else if (c.Events.Fact(MineQuestTag, Enmity, c.Here))
        {
            ShowPicture(c, FolkPicture);
            c.Screen.Text(3);
            c.CombatFlags |= 8;
            c.BattleMode = 1;
            c.Next = -4;
        }
        else
        {
            ShowPicture(c, DoorPicture);
            c.Screen.Text(0);
            ShowPicture(c, EnvoyPictures.First);
            c.Screen.Text(1);
            ShowPicture(c, EnvoyPictures.Second);
            c.Screen.Text(2);
            c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, Enmity, 0, 0, 0x2D0, 0, MineQuestTag);
            c.BattleMode = 1;
            c.Next = -3;
        }
        return c.Next;
    }
}
