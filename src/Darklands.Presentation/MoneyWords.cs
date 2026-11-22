using Darklands.Core;
using Darklands.Core.Economy;

namespace Darklands.Presentation;

/// <summary>
/// An amount in words from the money texts (GameTextIds.MoneyWords), as Money.ToString builds it in English: each coin there is
/// (florins, groschen, pfennigs, none when 0), then the joins of two or three of them. With the original's words it gives exactly
/// Money.ToString; a translation pack's give its language's, its plurals chosen by the count.
/// </summary>
// Not in the original: the 1.1 translations (a card's $Money in a translated text).
public static class MoneyWords
{
    /// <summary><paramref name="money"/> in words, <paramref name="get"/> giving a money text with its arguments.</summary>
    public static string Format(Money money, Func<string, IReadOnlyDictionary<string, object?>, string> get)
    {
        ArgumentNullException.ThrowIfNull(get);
        var parts = new List<string>(3);
        void Coin(string id, short count)
        {
            if (count > 0)
            {
                parts.Add(get(id, new Dictionary<string, object?>
                {
                    ["count"] = (int)count,
                    ["amount"] = count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                }));
            }
        }
        Coin(GameTextIds.MoneyFlorins, money.Florins);
        Coin(GameTextIds.MoneyGroschen, money.Groschen);
        Coin(GameTextIds.MoneyPfennigs, money.Pfennigs);
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            2 => get(GameTextIds.MoneyTwo, new Dictionary<string, object?> { ["a"] = parts[0], ["b"] = parts[1] }),
            _ => get(GameTextIds.MoneyThree, new Dictionary<string, object?> { ["a"] = parts[0], ["b"] = parts[1], ["c"] = parts[2] }),
        };
    }
}
