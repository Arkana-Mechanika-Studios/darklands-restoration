using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf02": a small dry cave under an overhanging ledge. When an item quest (tag 10) leads
/// here, the item lies inside, alone, guarded by a tatzelwurm, or in a bandits' lair, by the
/// place's number. The party can go in, watch the cave, or pray.
/// </summary>
// orig: R106+0x184A (19BC:000A) matched; tests: CardSmokeTests
// orig: R106+0x1F30 (19BC:06F0) matched (registration: saint pop-ups only, they do not name the member)
public sealed class SmallCaveUnderTheLedge : Card
{
    private const int CavePicture = 331;

    private const int Unguarded = 0;
    private const int Tatzelwurm = 1;
    // DORMANT: the guardian is never set to 2, so the watch's bandit sighting (text 13) never shows.
    private const int BanditsSeen = 2;
    private const int Bandits = 3;

    public override int Number => 250;

    public override int Run(CardContext c)
    {
        Show(c, CavePicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 9);
        // The guardian is kept in the game's saved-card word, as the original does.
        if (c.Events.QuestAt(10, c.Here))
        {
            // C: event_find does not set last_event; event_f2e_of(last_event) reads the stale global.
            _ = c.Events.Find(8, -1, c.Here, -1, 10, -1);
            c.NameToSlot(0, c.EventDetail(c.LastEvent), 0);
            // C: (cur_loc + 100) % 100, a signed remainder, non-negative for any location.
            var v = (c.Here + 100) % 100;
            c.State.SavedCard = v <= 25 ? Unguarded : v <= 50 ? Tatzelwurm : Bandits;
        }
        else
        {
            c.State.SavedCard = Unguarded;
        }
        c.SetCardSaints(0x11, 0x35, 0x44, 0x45);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R106+0x1CDE (line 0); hint R106+0x1D92 (19BC:0552) matched
            new MenuLine(_ => EnterTheCave(c), Unknown),
            // orig: R106+0x1DCA (line 1); hint R106+0x1E00 (19BC:05C0) matched
            new MenuLine(_ => WatchTheCave(c), Unknown),
            // orig: R106+0x1E38 (line 2); hint R106+0x1EA0 (19BC:0660) matched
            new MenuLine(_ => PrayForGuidance(c), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 3..8 are never offered; their actions R106+0x1ED8.. and hints
            // R106+0x1EDA.. are empty functions.
            // UNCERTAIN: those hints return whatever is in AX/DX; a neutral hint is used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            // orig: R106+0x1EF0 (19BC:06B0) matched; hint R106+0x1EF8 (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard, () => OptionHint.Say(SuccessAssured)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>
    /// Inside: nothing, the item alone (but the quest is not finished: an original oddity), the
    /// item and the tatzelwurm, or the item and the bandits.
    /// </summary>
    // orig: R106+0x1CDE (19BC:049E) matched; tests: CardSmokeTests
    // UNCERTAIN: the function's stack frame is unusual (read from the disassembly); the behaviour is plain.
    private static void EnterTheCave(CardContext c)
    {
        var guardian = c.State.SavedCard;
        if (guardian == Unguarded)
        {
            if (!c.Events.QuestAt(10, c.Here))
            {
                c.PassHours(1);
                c.Screen.Text(1);
                c.Next = CardContext.MapCard;
                return;
            }
            // The item is found (text 4), but FinishQuest10 is not called, as in the original.
            c.PassHours(1);
            c.Screen.Text(4);
            c.Next = CardContext.MapCard;
            return;
        }
        // C: `t == 1 && t`: the second test is redundant.
        if (guardian == Tatzelwurm)
        {
            c.PartyMaxAttribute((int)Attribute.Perception);
            c.Variables.Chosen[0] = c.BestMember;
            c.PassHours(1);
            c.FinishQuest10(c.Here);
            c.Screen.Text(2);
            FightTheTatzelwurm(c);
            return;
        }
        c.PassHours(1);
        c.FinishQuest10(c.Here);
        c.Screen.Text(3);
        FightTheBandits(c);
    }

    // orig: R106+0x1DCA (19BC:058A) matched; tests: CardSmokeTests
    private static void WatchTheCave(CardContext c)
    {
        c.PassHours(3);
        c.Screen.Text(c.State.SavedCard == BanditsSeen ? 13 : 12);
        c.Next = 250;
    }

    // orig: R106+0x1E38 (19BC:05F8) matched; tests: CardSmokeTests
    private static void PrayForGuidance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 1)
        {
            c.Screen.Text(9);
        }
        else if (c.State.SavedCard != Unguarded)
        {
            c.Screen.Text(10);
        }
        else
        {
            c.Screen.Text(c.Events.QuestAt(10, c.Here) ? 11 : 14);
        }
        c.Next = 250;
    }

    /// <summary>The bandits: won, a lesson in Virtue; fled, a night in hiding; beaten, those who ran come back robbed.</summary>
    // orig: R106+0x1AF2 (19BC:02B2) matched; tests: CardSmokeTests
    // C: right-to-left arguments: rand_mod(5) + 3 is the only draw before run_battle (seed -2 draws nothing).
    // DORMANT: text 15 (the beaten companions found alive) is never shown by this card.
    private static void FightTheBandits(CardContext c)
    {
        var bandits = c.Random(5) + 3;
        switch (c.RunBattle(0x67, -2, 7, 2, bandits, 0x1F, 3, 1, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                c.Screen.Text(8);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, -1);
                break;
            case 1:
            case 2:
                ShowPicture(c, 286);
                c.Screen.Text(5);
                c.PassHours(c.HoursUntil(5));
                break;
            case 3:
            case 4:
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.SetStatus(i, CharacterStatus.Able);
                        c.LoseItems(i);
                        c.Variables.Chosen[0] = i;
                        c.PracticeSkill(i, (int)Skill.Virtue, 0, 5, -1);
                    }
                }
                ShowPicture(c, 177);
                c.Screen.Text(7);
                break;
        }
        c.Next = CardContext.MapCard;
    }

    /// <summary>The tatzelwurm: won, a lesson in Virtue; otherwise it devours one who ran, and the rest flee.</summary>
    // orig: R106+0x1C28 (19BC:03E8) matched; tests: CardSmokeTests
    private static void FightTheTatzelwurm(CardContext c)
    {
        var wurms = c.Random(5) + 3;
        switch (c.RunBattle(0x66, -2, 0x3B, 2, wurms, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                c.Screen.Text(8);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, -1);
                break;
            case 1:
            case 2:
            case 3:
            case 4:
                // BUG: R029 2474 returns how many members were away, not who left: var_chosen1 gets that count, so the
                // text names party member [count], and the test against -1 always holds: the loss text and the Virtue
                // practice come even when nobody was away.
                c.Variables.Chosen[0] = c.OneOfTheAwayLeaves();
                if (c.Variables.Chosen[0] != -1)
                {
                    c.Screen.Text(6);
                    c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 0, 10, -1);
                }
                c.Screen.Text(5);
                c.PassHours(c.HoursUntil(5));
                break;
        }
        c.Next = CardContext.MapCard;
    }
}
