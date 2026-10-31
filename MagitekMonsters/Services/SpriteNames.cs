using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MagitekMonsters.Services;

public static class SpriteNames
{
    public static string Normalize(string value) => string.Concat(value.Replace("♀", "f", StringComparison.Ordinal)
        .Replace("♂", "m", StringComparison.Ordinal).Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
            && (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))).ToUpperInvariant();
}
