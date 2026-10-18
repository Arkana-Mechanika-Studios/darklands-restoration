namespace Darklands.Cards;

/// <summary>Names of the card pictures (PICS\<name>.PIC), by picture number.</summary>
// orig: pic_names = R136+0xC0A0, far data table: 360 far string pointers from 2CAF:0A47 (picture n is at 2CAF:0A47 + 4n; card scripts address picture n at pic_names+0x0A47+4n, e.g. +0x0F97 = 340, +0x0A57 = 4) with the strings after it (object R136 seg 2CAF); data, no source status; tests: CardSmokeTests (every card shows its picture)
// C: the 360 names below equal the string pool of R136 seg 2CAF in order (common ... xms129); the index of a script's pic_names+0xXXXX is (XXXX - 0xA47) / 4. Picture names repeat on purpose (e.g. "common" at 0 and 66, "xjbrown" twice): the table is not a set.
public static class PictureNames
{
    public static readonly string[] All =
    [
        "common", "bksmith", "xhouse3", "xnw3", "aproch1", "xaproch", "aproch2", "castle1",
        "xnw77", "dockday", "guard1", "house3", "knox", "stairs", "main-st", "thinh1",
        "xnost", "xgarg2", "garg1", "kaboom2", "boom", "xrc", "post-up", "backing",
        "robber", "robdead", "robhead", "village", "xchurch", "xcreep", "xcreep2", "xfire2",
        "xtower", "robmen2", "xside", "xbowyer", "xarmor", "xdamonk", "deadrob", "deadro2",
        "medici", "xcell", "xsewer2", "xsewer3", "xnw", "xwatch2", "xchase", "xgrove1",
        "xgrove27", "xnmain", "arive", "xlandho", "atax11", "virgin", "britter", "xsid",
        "din", "xvc1", "banker", "xelude", "xnchrch", "xhunt", "gatex", "hi-noon",
        "xnmonk", "wall", "common", "xkeep", "xdock", "cloth308", "cloth307", "baddalc",
        "baddalc2", "baddalc3", "alcem2", "alcem2", "gravs2", "xfit11", "xthanks", "xktempdk",
        "xkntevil", "xkntdemo", "xcastle2", "knghtbad", "knghtclo", "ktemplr", "ktemplr2", "melee",
        "xmtrange", "xarch", "xbudlght", "armr3", "xdchurch", "xdchrch2", "xghost", "xpain",
        "kngt", "xkntbad", "xktwood", "xmtrange", "xtree", "xwaterfa", "xjoustsk", "alccart9",
        "badal34", "goodal5", "xaltent", "xtown", "fit13", "xbigred", "xdrag", "xjbrown",
        "xjbrown", "dhead", "xbsmoke", "xbridge", "village3", "xdragkn", "nonite", "xdragkn",
        "xdnite", "xbaked3", "village4", "x2pig", "x2pig", "xshrat1", "xwolf", "xvilla",
        "xvilla3", "xdeer", "xwhunt", "xhound", "xmobglad", "monkbell", "xbook", "xbrnbook",
        "xdedmonk", "xdmnprnc", "xcook", "xdogwood", "path", "xbaptism", "xprestes", "xnytmare",
        "xfire", "xhidden", "xsumm", "xsumbook", "xsummalt", "xsummbrm", "xsummnng", "xsumoned",
        "xsummon", "xwfsntry", "xwolf3", "xoldman", "xuglyman", "xprtyldy", "xtipsey", "xservgil",
        "xmob", "xtempstk", "xwitbrgn", "xmtlake", "xdungeon", "xportcul", "xdesk", "xtempat1",
        "xtempgrd", "xchest", "xchestfl", "xtmplord", "xprisnr", "fountain", "xdedkght", "xaltar",
        "xaltar2", "xbandits", "xhall", "xfortmon", "xhallhrn", "xgrv1", "xgrv2", "xgrv3",
        "xgrv4", "xdoorway", "xdrway2", "xdrway3", "xhalltrp", "explshn", "xinkwell", "parched",
        "parched2", "parched3", "xpurse", "xtrschst", "weapons", "bottles", "bear", "xbear",
        "xcave", "xcave2", "xcave3", "xcave4", "xtroll2", "xrun", "xrun2", "xcave5",
        "xminer4", "xminer5", "xmqhole", "xmqhole2", "xkbold2", "xkbold2", "xcave6", "door21",
        "door22", "xdwarm", "xcoast", "xcoast2", "xriver", "xstmtide", "xwitch", "xpriest",
        "xhidden2", "banker2", "xredrg", "xsid", "baked2", "xvilage4", "xbaked3", "xms001",
        "xms002", "xms003", "xms004", "xms005", "xms006", "xms007", "ms008", "xms009",
        "xms010", "xms011", "xms012", "xms013", "xms014", "ms015", "ms016", "ms017",
        "ms018", "ms019", "ms020", "xms021", "xms022", "xms023", "xms024", "xms025",
        "xms026", "xms027", "xms028", "xms029", "xms030", "xms031", "xms032", "xms033",
        "ms034", "ms035", "xms036", "xms037", "ms038", "ms039", "ms040", "ms041",
        "ms042", "xms043", "xms044", "xms045", "xms046", "xms047", "xms048", "xms049",
        "xms050", "xms051", "xms052", "xms053", "xms054", "xms055", "xms056", "xms057",
        "xms058", "xms059", "xms060", "xms061", "xms062", "xms063", "xms064", "xms065",
        "xms066", "xms067", "xms068", "xms069", "xms070", "xms071", "xms072", "xms073",
        "xms074", "xms075", "xms076", "xms077", "xms078", "xms079", "xms080", "xms081",
        "xms082", "xms083", "xms084", "xms085", "xms086", "xms087", "xms088", "xms089",
        "xms090", "xms091", "xms092", "xms093", "xms094", "xms095", "xms096", "xms097",
        "xms098", "xms099", "xms100", "xms101", "xms102", "xms103", "xms104", "xms105",
        "xms106", "xms107", "xms108", "xms109", "xms110", "xms111", "xms112", "xms113",
        "xms114", "xms115", "xms116", "xms117", "xms118", "xms119", "ms120", "ms121",
        "xms122", "xms123", "xms124", "xms125", "xms126", "xms127", "xms128", "xms129",
    ];

    // DIVERGENCE: a picture number outside the table gives "" (an empty name, which the card engine shows as a blank screen); the original indexes the far table unchecked.
    public static string Of(int number) => number >= 0 && number < All.Length ? All[number] : "";
}
