namespace MagitekMonsters.Services;

public static class PveLossRules
{
    public const int Percent = 5;
    public const int MaximumLoss = 10000;
    public static int Cap(int highestLevel)
    {
        if (highestLevel is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(highestLevel));
        return highestLevel * 100;
    }

    public static int Loss(long balance, int cap)
    {
        if (balance < 0 || cap is < 0 or > MaximumLoss) throw new ArgumentOutOfRangeException();
        return balance == 0 || cap == 0 ? 0 : (int)Math.Min(balance, Math.Min(cap, Math.Max(1, balance / 20)));
    }
}
