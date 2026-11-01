namespace MagitekMonsters.Contracts;

public static class TrainerUid
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int Length = 10;
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "null", "nil", "admin", "moderator", "system", "staff", "support", "magitekmonsters" };
    public static bool TryNormalize(string? input, out string uid)
    {
        uid = (input ?? "").Trim().ToUpperInvariant();
        return uid.Length == Length && uid.All(Alphabet.Contains);
    }
    public static bool TryVanity(string? input, out string code)
    {
        code = (input ?? "").Trim();
        return code.Length is >= 3 and <= 15 && code.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
            && !Reserved.Contains(code) && !(code.Length > 3 && code.StartsWith("MM-", StringComparison.OrdinalIgnoreCase) && code[3..].All(char.IsAsciiDigit));
    }
    public static bool TryReference(string? input, out string code)
    {
        if (TryNormalize(input, out code)) return true;
        if (!TryVanity(input, out code)) return false;
        code = code.ToUpperInvariant(); return true;
    }
}
