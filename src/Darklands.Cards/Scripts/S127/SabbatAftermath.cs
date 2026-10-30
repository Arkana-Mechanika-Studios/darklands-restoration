using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>What the sabbat cards (R127/s128) share when the party leaves the sabbat site.</summary>
internal static class SabbatAftermath
{
    /// <summary>The world map.</summary>
    public const int WorldMap = 0x0C;

    /// <summary>"$Witcd21": the abandoned sabbat site, shown from now on when the party comes back.</summary>
    public const int AbandonedSite = 0x13D;

    /// <summary>"$Witcd38": the party overwhelmed by the witches wakes in the wilderness.</summary>
    public const int Overwhelmed = 0x14E;

    /// <summary>The site is abandoned from now on and the party is back on the map.</summary>
    public static void LeaveTheSite(CardContext c)
    {
        MarkTheSiteAbandoned(c);
        c.Next = WorldMap;
    }

    /// <summary>Coming back to this place shows the abandoned site (the original writes it without checking that the party is at a place).</summary>
    public static void MarkTheSiteAbandoned(CardContext c)
    {
        // DIVERGENCE: the C writes locations[location-now]->arrival card without a check; the C# skips it in the wilderness.
        if (c.HerePlace is { } place)
        {
            place.ArrivalCard = AbandonedSite;
        }
    }

    /// <summary>The fight is over: the party left the site or was overwhelmed.</summary>
    public static bool Over(CardContext c) => c.Next is Overwhelmed or WorldMap;

    /// <summary>
    /// The witches' flying ointment (fact 0x0D) lets the party flee before a fight (text
    /// <paramref name="offerBlock"/>, option line <paramref name="escapeLine"/>; the lines that
    /// are offered are <paramref name="lines"/>). Fleeing costs Virtue and Divine Favour, less
    /// when the innocents were saved (fact 9). Staying puts the scene's picture back.
    /// </summary>
    // orig: R127+0x0AC2 matched (and its twins in the other sabbat files)
    // C: the box writes card-option-enabled[] directly and re-points card-option-handler of its lines at "Success is assured." hints;
    //   card-option-flag-0..9 are untouched and no option handler runs: only the chosen line counts.
    public static void OfferTheOintment(CardContext c, int offerBlock, int escapeBlock, int escapeLine,
        int scenePicture, Func<int, OptionHint> hint, params int[] lines)
    {
        c.Screen.ShowPicture(PictureNames.Of(OintmentPicture));
        var states = new OptionState[CardMenu.MaxLines];
        foreach (var line in lines)
        {
            states[line] = OptionState.Enabled;
        }
        if (c.Screen.Choose(offerBlock, states, hint) != escapeLine)
        {
            c.Screen.ShowPicture(PictureNames.Of(scenePicture));
            return;
        }
        c.Screen.Text(escapeBlock);
        LeaveTheSite(c);
        if (c.Events.FactAnywhere(1, 9))
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -10);
        }
        else
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -25);
        }
    }

    public const int FightPicture = 160;
    public const int VictoryPicture = 146;
    private const int OintmentPicture = 149;
}
