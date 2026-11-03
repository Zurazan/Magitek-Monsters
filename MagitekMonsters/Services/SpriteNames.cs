using System;
using System.Text;

namespace MagitekMonsters.Services;

public static class SpriteNames
{
    public static string Normalize(string value)
    {
        foreach (var character in value)
        {
            if (character > 127)
            {
                value = value.Replace("♀", "f", StringComparison.Ordinal).Replace("♂", "m", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
                break;
            }
        }
        var length = 0; var unchanged = true;
        foreach (var character in value)
        {
            if (IsAsciiLetterOrDigit(character)) length++;
            if (character is not (>= 'A' and <= 'Z' or >= '0' and <= '9')) unchanged = false;
        }
        if (unchanged) return value;
        return string.Create(length, value, static (output, source) =>
        {
            var index = 0;
            foreach (var character in source)
                if (IsAsciiLetterOrDigit(character)) output[index++] = character is >= 'a' and <= 'z' ? (char)(character - 32) : character;
        });
    }
    private static bool IsAsciiLetterOrDigit(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
