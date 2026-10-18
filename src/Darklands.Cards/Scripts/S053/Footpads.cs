using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityT00": robbers. In a city street (or on the road just outside, when the party has just
/// left the map: the map's leave mode 3) footpads demand the party's goods. A watchful party
/// (a Perception roll) may be ambushed outright; otherwise it hands over its goods, talks its
/// way out, faces them down with its weapons, calls upon a saint, runs, throws a potion, or
/// fights. Everything ends at the card the street remembered (<c>SavedCard</c>), or on the map.
/// </summary>
// orig: R053+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: R053+0x0B68 (1838:0B68), the init, closes all 20 lines with the "screwed up."
//   handler R053+0x0BFE and installs pop-up handlers: the potion line's (items:
//   R053+0x0C36, R053+0x0C72, R053+0x0CAE,
//   R053+0x0CEA: "Success is assured.", naming the member) and the saint line's
//   (R053+0x0D26, R053+0x0DB0, R053+0x0E3A).
public sealed class Footpads : Card
{
    public const int CardNumber = 0x24;

    private const int Picture = 48, StreetPicture = 46, AlleyPicture = 3, ThreatPicture = 177, BladePicture = 249, PotionPicture = 189;

    private const int OverlandMap = 0x0C, NightEncounter = 0x3C;

    /// <summary>The map's leave mode when the party has just come off the road (DS:A891 = 3).</summary>
    private const int FromTheRoad = 3;

    /// <summary>An event that, once started, spares the party the ambush (kind 0x4F).</summary>
    private const int Spared = 0x4F;

    private const int Rope = 0x0F;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        var onTheRoad = c.MapLeaveMode == FromTheRoad;
        c.PlayMusic(onTheRoad ? 0x20 : 0x1F);
        Show(c, Picture);
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Enabled, OptionState.Enabled, OptionState.Enabled };
        c.PassHours(1);
        var perception = c.PartyMaxAttribute(3);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        if (!onTheRoad && c.State.SavedCard == NightEncounter)
        {
            return c.State.SavedCard;
        }
        // C: the && skips the event test when the roll fails to beat the Perception.
        if (c.Random(100) > perception && !c.Events.Started(Spared))
        {
            if (onTheRoad)
            {
                c.Screen.Text(0);
            }
            Fight(c, ambushed: true);
            if (onTheRoad)
            {
                c.State.CurrentLocation = -1;
                c.State.SavedCard = OverlandMap;
            }
            // C: the card returns saved_card, not the next card the fight chose (they are the same, or the map).
            return c.State.SavedCard;
        }
        var block = 1;
        if (onTheRoad)
        {
            block = 2;
            states[3] = OptionState.Hidden;
            states[5] = OptionState.Hidden;
        }
        c.SetCardItems(0x5F, 0x65, 0x61, 0x68);
        c.SetCardSaints(0x0C, 0x36, 0x3D);
        if (states[5] == OptionState.Enabled)
        {
            states[5] = c.ItemsUsable();
        }
        if (states[3] == OptionState.Enabled)
        {
            states[3] = c.SaintsUsable();
        }
        var menu = Menu(c, block);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        for (var k = 0; k <= 6; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        var next = menu.Run(
            // orig: R053+0x05A8 (1838:05A8) + R053+0x057E (1838:057E) R053 seg 1838 matched
            new MenuLine(() => HandOverTheGoods(c), () => OptionHint.Say(CardTexts.Unknown, c.Leader)),
            // orig: R053+0x06E2 (1838:06E2) + R053+0x05E2 (1838:05E2) R053 seg 1838 matched
            new MenuLine(chance => TalkYourWayOut(c, chance, onTheRoad), () => TalkChance(c, onTheRoad)),
            // orig: R053+0x0854 (1838:0854) + R053+0x074E (1838:074E) R053 seg 1838 matched
            new MenuLine(chance => FaceThemDown(c, chance, onTheRoad), () => WeaponChance(c)),
            // orig: R053+0x08EE (1838:08EE) + R053+0x0874 (1838:0874) R053 seg 1838 matched
            new MenuLine(_ => CallUponASaint(c, onTheRoad), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R053+0x0A56 (1838:0A56) + R053+0x0926 (1838:0926) R053 seg 1838 matched
            new MenuLine(chance => RunAway(c, chance, onTheRoad), () => RunChance(c)),
            // orig: R053+0x0AEE (1838:0AEE) + R053+0x0AA8 (1838:0AA8) R053 seg 1838 matched
            new MenuLine(_ => ThrowAPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            // orig: R053+0x0B30 (1838:0B30) + R053+0x0B26 (1838:0B26) R053 seg 1838 matched
            new MenuLine(() => Fight(c, ambushed: false), OptionHint.Saying(CardTexts.EnterCombat)));
        if (onTheRoad)
        {
            c.State.CurrentLocation = -1;
            next = OverlandMap;
            c.Next = next;
        }
        return next;
    }

    private static int StreetPictureFor(bool onTheRoad) => onTheRoad ? Picture : StreetPicture;

    private static void HandOverTheGoods(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        c.Screen.Text(3);
        c.AddItem(Who.Everybody, Rope);
        c.Next = c.State.SavedCard;
    }

    /// <summary>The leader's better of Charisma and Intelligence, plus half of Speak Common and Woodwise (Streetwise on the road).</summary>
    // C: as written, the road uses Streetwise and the city Woodwise (the practice below does the opposite).
    private static OptionHint TalkChance(CardContext c, bool onTheRoad)
    {
        var charisma = c.Attribute(Who.Leader, 5);
        var wits = c.Attribute(Who.Leader, 4);
        if (wits < charisma)
        {
            wits = charisma;
        }
        var lore = onTheRoad ? c.Skill(Who.Leader, 0x10) : c.Skill(Who.Leader, 0x12);
        return OptionHint.ChanceOf((c.Skill(Who.Leader, 0x0A) + lore) / 2 + wits, c.Leader);
    }

    private void TalkYourWayOut(CardContext c, int chance, bool onTheRoad)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(c.Leader, onTheRoad ? 0x12 : 0x10, 1, 10, -1);
            c.PracticeSkill(c.Leader, 0x0A, 1, 10, -1);
            ShowPicture(c, StreetPictureFor(onTheRoad));
            c.Screen.Text(4);
            c.PassHours(1);
            c.Next = c.State.SavedCard;
            return;
        }
        ShowPicture(c, ThreatPicture);
        c.Screen.Text(5);
        Show(c, Picture);
        Fight(c, ambushed: true);
    }

    /// <summary>The leader's best weapon skill (it also names him).</summary>
    private static OptionHint WeaponChance(CardContext c)
    {
        var chance = c.BestWeaponSkill(Who.Leader);
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>The leader shows his weapon (named in $NamedOneName, the hands when unarmed).</summary>
    private void FaceThemDown(CardContext c, int chance, bool onTheRoad)
    {
        c.Variables.Chosen[0] = c.Leader;
        var weapon = c.EquippedItem(c.Leader, 0);
        c.NameToSlot(0, weapon != -1 ? weapon : 7, 0);
        if (c.Random(100) <= chance)
        {
            c.BestWeaponSkill(Who.Leader);
            c.PracticeSkill(c.Variables.Chosen[0], (byte)c.BestSkillFound, 1, 10, -1);
            ShowPicture(c, StreetPictureFor(onTheRoad));
            c.Screen.Text(6);
            c.Next = c.State.SavedCard;
            return;
        }
        ShowPicture(c, BladePicture);
        c.Screen.Text(7);
        Show(c, Picture);
        Fight(c, ambushed: true);
    }

    // orig: R053+0x0D26, R053+0x0DB0, R053+0x0E3A (the saint pop-up handlers) R053 seg 1838 matched
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    /// <summary>Only an answer of exactly 1 helps; any other answer leaves the party facing the robbers.</summary>
    private static void CallUponASaint(CardContext c, bool onTheRoad)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, StreetPictureFor(onTheRoad));
            c.Screen.Text(8);
            c.Next = c.State.SavedCard;
        }
    }

    /// <summary>A mounted party rides off; on foot the slowest member's burdened Agility + 5.</summary>
    private static OptionHint RunChance(CardContext c) =>
        c.PartyGet(5) != 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(c.PartyMinEffectiveAgility() + 5, OptionHint.WholeParty);

    private void RunAway(CardContext c, int chance, bool onTheRoad)
    {
        if (c.PartyGet(5) != 0)
        {
            ShowPicture(c, StreetPictureFor(onTheRoad));
            c.Screen.Text(0x0A);
            c.Next = c.State.SavedCard;
        }
        else if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, StreetPictureFor(onTheRoad));
            c.Screen.Text(0x0B);
            c.Next = c.State.SavedCard;
        }
        else
        {
            c.PassHours(1);
            ShowPicture(c, StreetPictureFor(onTheRoad));
            c.Screen.Text(0x0C);
            Show(c, Picture);
            Fight(c, ambushed: true);
        }
    }

    // orig: R053+0x0C36, R053+0x0C72, R053+0x0CAE, R053+0x0CEA (the potion pop-up handlers) R053 seg 1838 matched
    private static OptionHint PotionHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.Say(SuccessAssured, c.MenuMember);
    }

    /// <summary>A potion scatters the robbers. (The potion is not used up: original.)</summary>
    // BUG: no take_item here: the potion stays in the pack.
    private static void ThrowAPotion(CardContext c)
    {
        ShowPicture(c, PotionPicture);
        c.Screen.Text(0x0D);
        c.PassHours(1);
        c.Next = c.State.SavedCard;
    }

    // C: outside any location (no city and not just off the road) place is -1: loc_reputation_adj reads the null record
    // C: locations[-1], draws its chance and writes nothing (LocationRules.AdjustReputation).
    private static void AdjustStanding(CardContext c, int place, int a, int b) => c.AdjustReputation(place, a, b);

    /// <summary>
    /// The fight (an ambush or the party's own attack): the robbers depend on the street the
    /// party came from (or the road); their number on the party's lore and size. A win raises
    /// the standing; fled, the members away rejoin; downed or surrendered, the party is robbed
    /// (left a rope).
    /// </summary>
    // orig: R053+0x02D6 (1838:02D6) R053 seg 1838 matched; checked against asm for the argument order
    private static void Fight(CardContext c, bool ambushed)
    {
        var onTheRoad = c.MapLeaveMode == FromTheRoad;
        var place = onTheRoad ? c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y) : c.Here;
        var robbers = c.State.PreviousCard switch
        {
            6 or 8 => ambushed ? 0x13 : 0x14,
            9 or 10 => ambushed ? 0x11 : 0x12,
            0x21 or 0x22 => ambushed ? 0x15 : 0x16,
            _ => ambushed ? 0x56 : 0x17,
        };
        if (onTheRoad)
        {
            robbers = ambushed ? 0x2E : 0x2F;
        }
        // C: arguments right to left: the lore level, the draw rand_mod(lore), the party's size, then the lore again.
        var draw = c.Random(c.PartyLoreLevel());
        var count = Math.Clamp(draw, c.PresentCount(), 8);
        var groups = c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(robbers, -1, 7, groups, count, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                AdjustStanding(c, place, 1, 5);
                ShowPicture(c, onTheRoad ? Picture : AlleyPicture);
                if (onTheRoad)
                {
                    c.Screen.Text(0x0F);
                }
                else
                {
                    c.Screen.Text(c.Random(3) switch { 0 => 0x0F, 1 => 0x11, _ => 0x12 });
                }
                c.Next = c.State.SavedCard;
                break;
            case 1 or 2:
                c.RejoinAway();
                ShowPicture(c, StreetPictureFor(onTheRoad));
                c.Screen.Text(0x0B);
                c.Next = c.State.SavedCard;
                break;
            case 3:
                AdjustStanding(c, place, -1, -2);
                c.LoseItems(Who.Everybody);
                c.AddItem(Who.Everybody, Rope);
                ShowPicture(c, Picture);
                c.Screen.Text(0x10);
                c.Next = c.State.SavedCard;
                c.PassHours(2);
                break;
            case 4:
                AdjustStanding(c, place, -1, -2);
                c.LoseItems(Who.Everybody);
                ShowPicture(c, onTheRoad ? Picture : ThreatPicture);
                c.Screen.Text(3);
                c.AddItem(Who.Everybody, Rope);
                c.Next = c.State.SavedCard;
                break;
            // DORMANT: other battle results change nothing.
        }
    }
}
