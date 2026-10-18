using System.Text;

namespace Darklands.Content.Formats.World;

/// <summary>
/// Decodes `DARKLAND.ALC`, the original alchemy formula data file.
/// </summary>
public sealed class AlchemyDecoder
{
    public const int FormulaRecordSize = 0x68;
    public const int DescriptionSize = 0x50;
    public const int MysticalNumberOffset = 0x50;
    public const int RiskFactorOffset = 0x52;
    public const int IngredientsOffset = 0x54;
    public const int IngredientSize = 0x04;
    public const int IngredientCount = 5;

    public Alchemy Decode(Stream stream, string name)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return DecodeBytes(memory.ToArray(), name);
    }

    public static Alchemy DecodeBytes(byte[] data, string name)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length == 0)
        {
            throw new InvalidDataException("DARKLAND.ALC is empty.");
        }

        var count = data[0];
        var requiredLength = 1 + (count * FormulaRecordSize);
        if (data.Length < requiredLength)
        {
            throw new InvalidDataException(
                $"DARKLAND.ALC declares {count} formula records but only contains {data.Length} bytes.");
        }

        var formulas = new AlchemyFormula[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 1 + (index * FormulaRecordSize);
            formulas[index] = DecodeFormula(data, offset, index);
        }

        return new Alchemy(name, formulas);
    }

    private static AlchemyFormula DecodeFormula(byte[] data, int offset, int index)
    {
        var description = DecodeNullTerminatedAscii(data, offset, DescriptionSize);
        var mysticalNumber = ReadUInt16(data, offset + MysticalNumberOffset);
        var riskFactor = ReadUInt16(data, offset + RiskFactorOffset);
        var ingredients = new List<AlchemyIngredient>(IngredientCount);
        var rawIngredientSlots = new AlchemyIngredient[IngredientCount];

        for (var ingredientIndex = 0; ingredientIndex < IngredientCount; ingredientIndex++)
        {
            var ingredientOffset = offset + IngredientsOffset + (ingredientIndex * IngredientSize);
            var quantity = ReadUInt16(data, ingredientOffset);
            var itemCode = ReadUInt16(data, ingredientOffset + 0x02);
            var ingredient = new AlchemyIngredient(quantity, itemCode);
            rawIngredientSlots[ingredientIndex] = ingredient;
            if (quantity == 0 && itemCode == 0)
            {
                continue;
            }

            ingredients.Add(ingredient);
        }

        return new AlchemyFormula(index, description, mysticalNumber, riskFactor, ingredients)
        {
            RawIngredientSlots = rawIngredientSlots,
        };
    }

    private static string DecodeNullTerminatedAscii(byte[] data, int offset, int size)
    {
        var length = 0;
        while (length < size && data[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(data, offset, length);
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8);
    }
}

