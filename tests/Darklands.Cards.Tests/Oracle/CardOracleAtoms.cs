using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// Aimed start-state changes for single cards, named in the variant of a run key (<c>L3-Vev28_3_10+sk15_60-O0</c>: the parts joined
/// by '+'). Each part is a prefix and numbers separated by '_'; the changes are applied to the golden-trace state before it is
/// exported (DARKLANDS_CARD_ORACLE_AIMED) or run by the C#:
/// <list type="bullet">
/// <item><c>hr&lt;h&gt;</c> the hour; <c>mo&lt;m&gt;</c> the month; <c>dy&lt;d&gt;</c> the day of the month</item>
/// <item><c>pu&lt;florins&gt;_&lt;groschen&gt;_&lt;pfennigs&gt;</c> the purse; <c>bn&lt;florins&gt;</c> the bank notes; <c>fa&lt;n&gt;</c> the fame; <c>rp&lt;n&gt;</c> the reputation here</item>
/// <item><c>fu&lt;n&gt;</c>, <c>me&lt;n&gt;</c>, <c>ha&lt;n&gt;</c> the standing with the Fugger, Medici, Hanse banks</item>
/// <item><c>sk&lt;skill&gt;_&lt;v&gt;</c> and <c>at&lt;attribute&gt;_&lt;v&gt;</c> every member's skill / attribute (and its maximum)</item>
/// <item><c>sa&lt;n&gt;</c> every member knows saint n; <c>fo&lt;n&gt;</c> every member knows formula n; <c>it&lt;code&gt;</c> every member carries item code</item>
/// <item><c>st&lt;member&gt;_&lt;status&gt;</c> a member's status; <c>sz&lt;n&gt;</c> the party size (members dropped from the end); <c>ld&lt;member&gt;</c> the leader</item>
/// <item><c>ev&lt;kind&gt;_&lt;type&gt;_&lt;tag&gt;_&lt;value&gt;_&lt;owner&gt;_&lt;location&gt;</c> a started event (trailing numbers optional: type 0, tag 0, value 0 (9999: the current place), owner -2, here; location -1: nowhere in particular, 0..: that place)</item>
/// <item><c>ps&lt;n&gt;</c> the party's seed (<c>Party.Seed</c>, apart from the generator's <c>rn</c>); <c>px&lt;n&gt;</c>, <c>py&lt;n&gt;</c> the map position (DS:E3D0 / DS:E3D2)</item>
/// <item><c>ed&lt;d1&gt;_&lt;d2&gt;_&lt;d3&gt;</c> the three data words of the event created last (<c>ev..</c> before it); <c>er&lt;kind&gt;_&lt;type&gt;_&lt;tag&gt;</c> removes the matching events (-1: any)</item>
/// <item><c>sq</c>, <c>sr</c> the first / second saved card = the slot of the event created last (a card that names a quest event slot)</item>
/// <item><c>iq&lt;code&gt;_&lt;quality&gt;_&lt;count&gt;</c> every member carries that stack (quality and count as given); <c>ac&lt;attribute&gt;_&lt;v&gt;</c> the current attribute alone (the maximum stays)</item>
/// <item><c>rn&lt;seed&gt;</c> the random generator's seed; <c>ml&lt;n&gt;</c> the map-leave mode; <c>pc&lt;n&gt;</c> the previous card (prev_screen); <c>sc&lt;n&gt;</c>, <c>sd&lt;n&gt;</c> the two saved cards</item>
/// </list>
/// </summary>
public static class CardOracleAtoms
{
    public static bool Apply(Core.GameState state, string atom)
    {
        var at = 0;
        while (at < atom.Length && char.IsLetter(atom[at]))
        {
            at++;
        }
        var prefix = atom[..at];
        int[] n;
        try
        {
            n = atom.Length == at ? [] : atom[at..].Split('_').Select(int.Parse).ToArray();
        }
        catch (FormatException)
        {
            return false;
        }
        var members = state.Party.Members;
        switch (prefix)
        {
            case "hr":
                state.Date = state.Date with { Hour = (short)n[0] };
                return true;
            case "mo":
                state.Date = state.Date with { Month = (short)n[0] };
                return true;
            case "dy":
                state.Date = state.Date with { Day = (short)n[0] };
                return true;
            case "pu":
                state.Party.Purse = Money.From(n[0], n.Length > 1 ? n[1] : 0, n.Length > 2 ? n[2] : 0);
                return true;
            case "bn":
                state.Party.BankNotes = Money.From(n[0], 0, 0);
                return true;
            case "fa":
                state.Party.Fame = n[0];
                return true;
            case "rp":
                if (state.Here is { } here)
                {
                    here.Reputation = n[0];
                }
                return true;
            case "fu":
                state.FuggerStanding = n[0];
                return true;
            case "me":
                state.MediciStanding = n[0];
                return true;
            case "ha":
                state.HanseStanding = n[0];
                return true;
            case "sk":
                foreach (var m in members)
                {
                    m.Skills[n[0]] = n[1];
                }
                return true;
            case "at":
                foreach (var m in members)
                {
                    m.Attributes[n[0]] = m.AttributeMaximums[n[0]] = n[1];
                }
                return true;
            case "sa":
                foreach (var m in members)
                {
                    m.Saints.Add(n[0]);
                }
                return true;
            case "fo":
                foreach (var m in members)
                {
                    m.Formulas.Add(n[0]);
                }
                return true;
            case "it":
                var defs = state.Data.ItemDefinitions;
                var d = defs.FirstOrDefault(x => x.Code == n[0]);
                if (d is null)
                {
                    return false;
                }
                foreach (var m in members)
                {
                    if (m.Pack.Count < Pack.Size - 1)
                    {
                        m.Pack.Add(new ItemStack(d.Code, d.Type, 25, 1, d.Weight));
                    }
                }
                return true;
            case "st":
                if (n[0] < members.Count)
                {
                    members[n[0]].Status = (CharacterStatus)n[1];
                }
                return true;
            case "sz":
                while (members.Count > n[0] && members.Count > 1)
                {
                    members.RemoveAt(members.Count - 1);
                }
                state.Party.LeaderIndex = Math.Min(state.Party.LeaderIndex, members.Count - 1);
                return true;
            case "ld":
                state.Party.LeaderIndex = Math.Min(n[0], members.Count - 1);
                return true;
            case "ev":
                state.Events.Create(n.Length > 4 ? n[4] : -2, n.Length > 5 ? n[5] : state.CurrentLocation, 0, n[0], n.Length > 1 ? n[1] : 0,
                    n.Length > 3 ? (n[3] == 9999 ? state.CurrentLocation : n[3]) : 0, 0, EventLog.Forever, 0, n.Length > 2 ? n[2] : 0);
                return true;
            case "ps":
                state.Party.Seed = n[0];
                return true;
            case "px":
                state.MapPosition = state.MapPosition with { X = n[0] };
                return true;
            case "py":
                state.MapPosition = state.MapPosition with { Y = n[0] };
                return true;
            case "ed":
                if (state.Events[state.Events.Last] is { } last)
                {
                    last.Data1 = n[0];
                    last.Data2 = n.Length > 1 ? n[1] : 0;
                    last.Data3 = n.Length > 2 ? n[2] : 0;
                }
                return true;
            case "er":
                state.Events.Remove(n[0], n.Length > 1 ? n[1] : -1, -1, -1, n.Length > 2 ? n[2] : -1);
                return true;
            case "iq":
                var qd = state.Data.ItemDefinitions.FirstOrDefault(x => x.Code == n[0]);
                if (qd is null)
                {
                    return false;
                }
                foreach (var m in members)
                {
                    if (m.Pack.Count < Pack.Size - 1)
                    {
                        m.Pack.Add(new ItemStack(qd.Code, qd.Type, n[1], n.Length > 2 ? n[2] : 1, qd.Weight));
                    }
                }
                return true;
            case "ac":
                foreach (var m in members)
                {
                    m.Attributes[n[0]] = n[1];
                }
                return true;
            case "rn":
                (state.Random as Core.Randomness.MscRandom)?.Seed((uint)n[0]);
                return true;
            case "ml":
                state.MapLeaveMode = n[0];
                return true;
            case "pc":
                state.PreviousCard = n[0];
                return true;
            case "sc":
                state.SavedCard = n[0];
                return true;
            case "sq":
                state.SavedCard = state.Events.Last;
                return true;
            case "sr":
                state.SavedCard2 = state.Events.Last;
                return true;
            case "sd":
                state.SavedCard2 = n[0];
                return true;
            default:
                return false;
        }
    }
}
