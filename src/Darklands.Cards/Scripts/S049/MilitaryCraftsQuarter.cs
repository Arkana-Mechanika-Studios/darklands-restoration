namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MilCr00": the streets of the arms and armor guilds. The party turns toward the Blacksmiths'
/// street, Swordsmith's Lane, Armorer's Way or the Square of Bowyers and Gunsmiths to shop (the
/// guilds close their doors when the city is in revolt or in the evening), or to the other
/// quarters of the city.
/// </summary>
// orig: R049+0x2E2C (1B1A:000C) matched (checked against asm: the C's far-call arguments are wrong); tests: CardSmokeTests
public sealed class MilitaryCraftsQuarter : Card
{
    public const int CardNumber = 0x17;

    private const int Picture = 36;

    private const int OtherQuarters = 0x7C, Crafts = 0x1A, SideStreet = 0x09, SideStreetAtNight = 0x0A, MainStreet = 0x06,
        MainStreetAtNight = 0x08, Specific = 0x62;

    private const int Politics = 2;

    /// <summary>Set once a revolt was seen in the city: the guilds then stay shut (the module flag, never cleared while the game runs).</summary>
    // C: DS:58A3 = 0 in the EXE's initial data.
    private bool _revoltSeen;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x0C);
        Show(c, Picture);
        if (c.Events.QuestAt(Politics, c.Here))
        {
            _revoltSeen = true;
        }
        var states = new OptionState[10];
        states[1] = states[2] = states[3] = states[4] = states[6] = states[7] = states[8] = states[9] = OptionState.Enabled;
        // C: option 0 starts hidden and this test can only hide it again: Soldier's Road is never offered.
        if (c.LocationQuery(c.Here, 0x20) != 1 && c.LocationQuery(c.Here, 0x20) != 2)
        {
            states[0] = OptionState.Hidden;
        }
        if (c.LocationQuery(c.Here, 4) == 0)
        {
            states[1] = OptionState.Hidden;
        }
        if (c.LocationQuery(c.Here, 5) == 0)
        {
            states[2] = OptionState.Hidden;
        }
        if (c.LocationQuery(c.Here, 7) == 0)
        {
            states[3] = OptionState.Hidden;
        }
        if (c.LocationQuery(c.Here, 6) == 0)
        {
            states[4] = OptionState.Hidden;
        }
        // C: open-quest-at(...) != 1 is "no quest here" (it returns 0 or 1).
        if (!c.Events.QuestAt(Politics, c.Here))
        {
            states[6] = OptionState.Hidden;
        }
        // DORMANT: R049+0x36B4 (1B1A:0894) is empty and R049+0x36B6 (1B1A:0896) ("screwed up.") is the default handler of
        // the option lines the card does not use.

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 6, 7, 8, 9);
        foreach (var k in new[] { 0, 1, 2, 3, 4, 6, 7, 8, 9 })
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        menu.Run(
            // DORMANT: option 0 (Soldier's Road, the Arms Outfitter) is never enabled.
            // orig: R049+0x310E (1B1A:02EE) matched
            // orig: R049+0x315C (1B1A:033C) matched
            new MenuLine(() => SoldiersRoad(c), OptionHint.Saying(SuccessAssured)),
            // orig: R049+0x3194 (1B1A:0374) matched
            // orig: R049+0x3242 (1B1A:0422) matched
            Shop(c, 0x14, 0x64, 0, 0xFF, 0x400, 0x4B),
            // orig: R049+0x327A (1B1A:045A) matched
            // orig: R049+0x3326 (1B1A:0506) matched
            Shop(c, 0x13, 0x5A, 2, 0x0F, 0, 0x4F),
            // orig: R049+0x335E (1B1A:053E) matched
            // orig: R049+0x340C (1B1A:05EC) matched
            Shop(c, 0x12, 0x5F, 3, 0xC0, 0x400, 0x51),
            // orig: R049+0x3444 (1B1A:0624) matched
            // orig: R049+0x34F2 (1B1A:06D2) matched
            Shop(c, 0x11, 0x55, 5, 0x30, 0x83C, 0x55),
            // DORMANT: option 5 (it has no text line) is never enabled.
            // orig: R049+0x352A (1B1A:070A) matched
            // orig: R049+0x353C (1B1A:071C) matched
            Walk(c, () => Specific),
            // orig: R049+0x3574 (1B1A:0754) matched
            // orig: R049+0x3586 (1B1A:0766) matched
            Walk(c, () => OtherQuarters),
            // orig: R049+0x35BE (1B1A:079E) matched
            // orig: R049+0x35D0 (1B1A:07B0) matched
            Walk(c, () => Crafts),
            // orig: R049+0x3608 (1B1A:07E8) matched
            // orig: R049+0x3626 (1B1A:0806) matched
            Walk(c, () => c.IsDaylight ? SideStreet : SideStreetAtNight),
            // orig: R049+0x365E (1B1A:083E) matched
            // orig: R049+0x367C (1B1A:085C) matched
            Walk(c, () => c.IsDaylight ? MainStreet : MainStreetAtNight));
        return c.Next;
    }

    private static MenuLine Walk(CardContext c, Func<int> destination) =>
        new(() =>
            {
                c.PassHours(0);
                c.Next = destination();
            },
            OptionHint.Saying(SuccessAssured));

    /// <summary>
    /// The Arms Outfitter on Soldier's Road: a text by the parity of the city's third value, then
    /// the shop (it sells everything of the arms categories), two hours.
    /// </summary>
    private void SoldiersRoad(CardContext c)
    {
        c.Screen.Text(c.HereQuery(0x21) % 2 == 0 ? 1 : 2, TextFrame.BlankPage);
        c.Barter(1, (long)((ushort)0xFF | ((long)(ushort)0x400 << 16)), "Arms Outfitter");
        c.PassHours(2);
        c.Next = CardNumber;
    }

    /// <summary>
    /// A guild's shop: open by day when no revolt was seen and a sum of the city's local value, the
    /// party's fame (over 20), half the standing and the party's average Charisma stays below a
    /// limit; else the doors are shut and the party goes to a card telling so (day or night).
    /// </summary>
    private MenuLine Shop(CardContext c, int offset, int limit, int shop, int lowMask, int highMask, int closedCard) =>
        new(() =>
            {
                if (!_revoltSeen && c.IsDaylight)
                {
                    // C: the sum is a 16-bit unsigned value (the modulus is unsigned, so a negative standing
                    // wraps it around and the shop stays closed); the calls run in the order Charisma, standing,
                    // fame, local value.
                    var charisma = c.PartyAverageAttribute(5);
                    var standing = c.HereQuery(0);
                    var fame = c.PartyGet(4);
                    var local = c.HereQuery(0x21);
                    var sum = (ushort)((ushort)((ushort)(local + offset) % 0x15) + fame / 0x14 + standing / 2 + charisma);
                    if (sum < limit)
                    {
                        c.Barter(shop, (long)((ushort)lowMask | ((long)(ushort)highMask << 16)), null);
                        c.PassHours(1);
                        c.Next = CardNumber;
                        return;
                    }
                }
                c.Next = c.IsDaylight ? closedCard : closedCard + 1;
            },
            OptionHint.Saying(SuccessAssured));
}
