namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:C3CC: a reload keeps the frame (set only by enemy_anim_load's reloading, which is not needed here).</summary>
    // DORMANT: the sprite buffers never need reloading here (no EMS), so the frame is always reset.
    private readonly bool _keepFrame = false;

    /// <summary>
    /// Sets the animation mode (cbt_set_anim): loads the file "code + WK/CB/DY + weapon" into the
    /// slot when another one is there, and sets up the frame sequence (walk 0..9; attack the
    /// HITSEQ row; hit 0, 1, 2; dying 0..n).
    /// </summary>
    // orig: cbt_set_anim R014+0x38B6 (0E76:38B6) draft (byte-matched per the draft notes)
    public void SetPose(int i, Pose mode)
    {
        var c = Battle[i];
        var old = c.Pose;
        var kind = c.AnimationSlot;
        var slot = kind;
        string posture;
        switch (mode)
        {
            case Pose.Fighting or Pose.Hit:
                posture = "CB";
                slot += kind >= Combatant.PartySlots ? 2 : 0;
                break;
            case Pose.Dying:
                posture = "DY";
                slot += kind >= Combatant.PartySlots ? 4 : 0;
                break;
            case Pose.Walking:
                posture = "WK";
                break;
            default:
                posture = "";
                break;
        }
        var weapon = mode != Pose.Dying ? SlotWeapons[kind] : "";
        var name = SlotCodes[kind] + posture + weapon;
        if (old == mode && string.Equals(name, Slots[slot].FileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        c.Pose = mode;
        var loaded = string.Equals(name, Slots[slot].FileName, StringComparison.OrdinalIgnoreCase);
        switch (mode)
        {
            case Pose.Walking:
                if (!loaded)
                {
                    LoadAnimation(i, slot, SlotCodes[kind], posture, weapon);
                }
                c.FollowSteps = -1;
                c.SequenceLength = Slots[kind].FrameCount;
                for (var k = 0; k <= 9; k++)
                {
                    c.Sequence[k] = k;
                }
                if (!_keepFrame)
                {
                    c.Frame = 0;
                }
                return;
            case Pose.Fighting:
                if (!loaded)
                {
                    LoadAnimation(i, slot, SlotCodes[kind], posture, weapon);
                }
                c.SequenceLength = Slots[slot].AttackLength;
                for (var k = 0; k < c.SequenceLength && k < c.Sequence.Length; k++)
                {
                    c.Sequence[k] = Slots[slot].Sequences[k];
                }
                break;
            case Pose.Hit:
                if (!loaded)
                {
                    LoadAnimation(i, slot, SlotCodes[kind], posture, weapon);
                }
                c.SequenceLength = 2;
                for (var k = 0; k <= 2; k++)
                {
                    c.Sequence[k] = k;
                }
                break;
            case Pose.Dying:
                if (!loaded)
                {
                    LoadAnimation(i, slot, SlotCodes[kind], posture, "");
                }
                // C: party members keep the length the load left (the file's frame count, or the last pose's when it was loaded already);
                // only enemies take DS:C90C[slot].
                if (kind >= Combatant.PartySlots)
                {
                    c.SequenceLength = Slots[slot].FrameCount;
                }
                // C: the loop writes len + 1 entries (k <= len) into the 10-byte sequence.
                for (var k = 0; k <= c.SequenceLength && k < c.Sequence.Length; k++)
                {
                    c.Sequence[k] = k;
                }
                break;
            default:
                return;
        }
        if (!_keepFrame)
        {
            c.Frame = -1;
        }
    }

    /// <summary>sprite_file_load: the file's header values into the slot, its frame count also combatant i's sequence length.</summary>
    // orig: sprite_file_load R014+0x3D2C (0E76:3D2C) draft
    // DIVERGENCE: a file missing from the catalogues leaves the slot as it was (the original would read garbage).
    private void LoadAnimation(int i, int slot, string code, string posture, string weapon)
    {
        // C: the switch on the code's first letter (upper-cased when above '_') picks the catalogue; in the monsters' case
        // (0x3F76..0x3FB7) a file of M73 (the seven-headed dragon) sets DS:C21A to 'I', of M90 (the cave dragon) to 'Z' — every
        // load of such a file, before it is read; nothing clears it until the next battle.
        if (code.Length >= 3 && (code[0] > '_' ? code[0] - 0x20 : code[0]) == 'M')
        {
            if (code[1] == '7' && code[2] == '3')
            {
                Battle.DragonFight = Battle.HydraFight;
            }
            else if (code[1] == '9' && code[2] == '0')
            {
                Battle.DragonFight = Battle.CaveDragonFight;
            }
        }
        if (_library.Find(code, posture, weapon) is { } info)
        {
            Slots[slot].Load(info);
            // C: cbt_anim_len[i] = DS:C908[slot] = the header's frame count (the dying pose of a party member keeps it: R014+0x3C21
            // sets the length for enemies only)
            Battle[i].SequenceLength = Slots[slot].FrameCount;
        }
    }

    /// <summary>Readies a weapon and records its animation code for the combatant's slot (weapon_ready).</summary>
    private void ReadyWeapon(int i, int weapon, int missileFlag) =>
        Attacks.ReadyWeapon(Battle, i, weapon, missileFlag, code => SlotWeapons[Battle[i].AnimationSlot] = code);

    /// <summary>The frame of the animation file to draw for combatant i (cbt_anim_seq[f1b]).</summary>
    public int ShownFrame(int i)
    {
        var c = Battle[i];
        var f = Math.Clamp(c.Frame, 0, c.Sequence.Length - 1);
        return c.Sequence[f];
    }

    /// <summary>The animation file name shown for combatant i.</summary>
    public (string Code, string Posture, string Weapon) ShownFile(int i)
    {
        var c = Battle[i];
        var kind = c.AnimationSlot;
        return c.Pose switch
        {
            Pose.Fighting or Pose.Hit => (SlotCodes[kind], "CB", SlotWeapons[kind]),
            Pose.Dying => (SlotCodes[kind], "DY", ""),
            _ => (SlotCodes[kind], "WK", SlotWeapons[kind]),
        };
    }

    /// <summary>Releases enemy slot <paramref name="kind"/> when no other enemy uses it (1DB7:0418).</summary>
    // orig: R014+0xF828 (1DB7:0418) draft (near-match per the draft notes); the slot's buffers
    // freed by enemy_anim_free6 R025+0x0C02 matched (and its file names cleared)
    private void ReleaseEnemySlot(int i, int kind)
    {
        for (var k = Combatant.PartySlots; k < Combatant.Count; k++)
        {
            if (k != i && Battle[k].Status != Characters.CharacterStatus.Empty && Battle[k].AnimationSlot == kind)
            {
                return;
            }
        }
        if (kind is 5 or 6)
        {
            EnemySlotIdentity[kind - 5] = -1;
            foreach (var s in new[] { kind, kind + 2, kind + 4 })
            {
                Slots[s] = new AnimationSlot();
            }
        }
    }

    /// <summary>enemy_anim_free5: both enemy slots free.</summary>
    // orig: enemy_anim_free5 R025+0x0BE4 (1EF8:0BE4) draft
    private void FreeEnemySlots()
    {
        EnemySlotIdentity[0] = EnemySlotIdentity[1] = -1;
        for (var s = 5; s < Slots.Length; s++)
        {
            Slots[s] = new AnimationSlot();
        }
    }

    /// <summary>
    /// "Back to the melee weapon" before an archer or caster closes in: alone of its look it readies the hand weapon in its own
    /// slot (which then holds the melee look); else it moves to the slot that holds (or can take) its melee look, so the others
    /// keep theirs.
    /// </summary>
    // orig: R014+0xF9D8 (1DB7:05C8) draft
    private void BackToMelee(int i)
    {
        var c = Battle[i];
        var n = 0;
        for (var j = Combatant.PartySlots; j < Combatant.Count; j++)
        {
            if (Battle[j].Status != Characters.CharacterStatus.Empty && c.Appearance == Battle[j].Appearance)
            {
                n++;
            }
        }
        if (n == 1)
        {
            ReadyWeapon(i, c.DefaultWeapon, 0);
            c.Frame = 0;
            c.EnemyClass = 0;
            EnemySlotIdentity[c.AnimationSlot == 5 ? 0 : 1] = c.Appearance;
            return;
        }
        var k = c.AnimationSlot;
        // C: DS:D680 is only written for missile users; another enemy reads what the record's last occupant left (0 in a fresh
        // program, which would be party slot 0's animation).
        if (c.PreferredSlot != -1)
        {
            c.AnimationSlot = c.PreferredSlot;
            if (!SlotLoaded(c.AnimationSlot))
            {
                ReadyWeapon(i, c.DefaultWeapon, 0);
                LoadEnemyAnimation(i);
                c.Frame = 0;
            }
        }
        else
        {
            MoveToFreeSlot(i);
        }
        c.EnemyClass = 0;
        if (c.AnimationSlot != k)
        {
            ReleaseEnemySlot(i, k);
        }
    }

    /// <summary>
    /// The other enemy slot, when free, takes this enemy's look (code, weapon, name) under its melee identity; the others of
    /// the old slot note it as theirs to move to; then the melee animation is loaded.
    /// </summary>
    /// <returns>The new slot, or 0 when the other slot is taken.</returns>
    // orig: R014+0xF88E (1DB7:047E) unmatched
    private int MoveToFreeSlot(int i)
    {
        var c = Battle[i];
        var k = c.AnimationSlot;
        var n = 0;
        if (k == 5 && EnemySlotIdentity[1] == -1)
        {
            n = 6;
            EnemySlotIdentity[1] = c.Appearance;
        }
        else if (k == 6 && EnemySlotIdentity[0] == -1)
        {
            n = 5;
            EnemySlotIdentity[0] = c.Appearance;
        }
        if (n == 0)
        {
            return 0;
        }
        SlotCodes[n] = SlotCodes[k];
        SlotWeapons[n] = SlotWeapons[k];
        c.AnimationSlot = n;
        _slotKind[n] = _slotKind[k];
        for (var j = Combatant.PartySlots; j < Combatant.Count; j++)
        {
            if (Battle[j].IsAble && Battle[j].AnimationSlot == k)
            {
                Battle[j].PreferredSlot = n;
            }
        }
        if (!SlotLoaded(n))
        {
            ReadyWeapon(i, c.DefaultWeapon, 0);
            LoadEnemyAnimation(i);
            c.Frame = 0;
        }
        return n;
    }

    /// <summary>anim_buffers[slot] != 0: the slot holds an animation.</summary>
    private bool SlotLoaded(int slot) => Slots[slot].HasBuffer;

    /// <summary>
    /// After the loot screen: the enemy animations taken up again. The six enemy slots forget their files, each enemy record in use
    /// sets its pose again (its frame sequence from the file; walking: no follow steps), and every combatant keeps its frame and sequence length.
    /// </summary>
    // orig: loot_screen R014+0xE3C0 (19CD:2E50) unmatched (its enemy animations; checked against the asm)
    // C: DS:C96B[0..12] (cbt_anim_len) and record +0x1B (the frame) of the 13 combatants are saved at the start (0xE3D7) and put back at
    // C: the end (0xE741), DS:D65F / DS:D67E (the enemy slots' identities) the same; enemy_anim_free5 at the start (0xE43C) frees the enemy
    // C: slots' buffers; after loot_distribute the names of slots 5..10 are cleared (0xE6C0, DS:11BC..DS:11C6), then for i = 5..12 with
    // C: record +0x14 != 0: if anim_buffers[+0x16] == 0 enemy_anim_alloc(+0x16), and cbt_set_anim(i, +0x18) (0xE703)
    // C: the tiles reloaded after it (battle_tiles_load, and the varying furniture when its buffer was freed) are the same tiles: the
    // C: original's memory, nothing to model
    public void TakeUpEnemyAnimations()
    {
        var kept = Battle.Combatants.Select(c => (c.Frame, c.SequenceLength)).ToArray();
        Slots[5].HasBuffer = Slots[6].HasBuffer = false;
        for (var s = 5; s <= 10; s++)
        {
            Slots[s].ForgetFile();
        }
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status == Characters.CharacterStatus.Empty)
            {
                continue;
            }
            Slots[c.AnimationSlot].HasBuffer = true;
            SetPose(i, c.Pose);
        }
        for (var i = 0; i < Combatant.Count; i++)
        {
            (Battle[i].Frame, Battle[i].SequenceLength) = kept[i];
        }
    }

    /// <summary>enemy_anim_load(kind, who): the slot's files and the enemy in its walking pose.</summary>
    // orig: enemy_anim_load R025+0x09A6 (1EF8:09A6) matched; its buffers by enemy_anim_alloc
    // R025+0x0A84 matched (the sizes from TACANIM.DB: memory, nothing to model)
    // DIVERGENCE: when a higher slot's buffer lies above this one in memory the original frees both enemy slots and sets every
    // enemy's current pose again keeping its frame (a heap-layout case); here the plain branch is always taken.
    private void LoadEnemyAnimation(int who) => SetPose(who, Pose.Walking);
}
