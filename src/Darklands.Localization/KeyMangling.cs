namespace Darklands.Localization;

/// <summary>
/// Turns the game's string keys (<c>exe:DS:4F48</c>, <c>msg:$CITYG04#3</c>, <c>lst:item/42.name</c>,
/// <c>rp:camp.spend-day</c>) into the Fluent identifiers a translator writes in a <c>.ftl</c> file, made to be read
/// by people: letters are folded to lower case, <c>$</c> is dropped, and <c>:</c>, <c>.</c>, <c>/</c>, <c>#</c> and
/// <c>+</c> become a hyphen; letters, digits, hyphen and underscore stay. So <c>exe:DS:4F48</c> is <c>exe-ds-4f48</c>,
/// <c>msg:$CITYG04#3</c> is <c>msg-cityg04-3</c> and <c>lst:item/42.name</c> is <c>lst-item-42-name</c>.
/// <para>
/// The mapping only goes one way: the localizer mangles the key it is asked for and looks that up, so it never
/// needs to turn an identifier back into a key. Two keys could in principle meet in one identifier
/// (<c>rp:a.b</c> and <c>rp:a-b</c>); <see cref="Collisions"/> finds such pairs in a set of keys. The 9,086 keys the
/// original-text extractor builds from the English 483.07 files have none; the loader will check again at run time.
/// </para>
/// </summary>
public static class KeyMangling
{
    /// <summary>The Fluent identifier for <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">The key is empty, does not start with an ASCII letter, or holds a character
    /// other than ASCII letters, digits and <c>: . / # + $ - _</c>.</exception>
    public static string ToFluentId(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0 || !char.IsAsciiLetter(key[0]))
        {
            throw new ArgumentException("A key must start with an ASCII letter.", nameof(key));
        }

        return string.Create(key.Length - Count(key, '$'), key, static (span, k) =>
        {
            var at = 0;
            foreach (var c in k)
            {
                switch (c)
                {
                    case '$':
                        break;
                    case ':' or '.' or '/' or '#' or '+':
                        span[at++] = '-';
                        break;
                    case '-' or '_':
                    case >= '0' and <= '9':
                    case >= 'a' and <= 'z':
                        span[at++] = c;
                        break;
                    case >= 'A' and <= 'Z':
                        span[at++] = (char)(c + ('a' - 'A'));
                        break;
                    default:
                        throw new ArgumentException("Character '" + c + "' is not allowed in a key ('" + k + "').", nameof(key));
                }
            }
        });
    }

    /// <summary>The groups of keys in <paramref name="keys"/> that share one Fluent identifier (empty when none do).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Collisions(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var byId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            var id = ToFluentId(key);
            if (!byId.TryGetValue(id, out var list))
            {
                byId[id] = list = [];
            }

            list.Add(key);
        }

        return byId.Values.Where(l => l.Count > 1).Select(l => (IReadOnlyList<string>)l).ToList();
    }

    private static int Count(string s, char c)
    {
        var n = 0;
        foreach (var x in s)
        {
            if (x == c)
            {
                n++;
            }
        }

        return n;
    }
}
