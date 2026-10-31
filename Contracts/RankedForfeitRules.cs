namespace MagitekMonsters.Services;

public static class RankedForfeitRules
{
    public const int FirstRewardedTurn = 3;
    public const int Limit = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    public static bool IsEarly(int turn) => turn >= 0 && turn < FirstRewardedTurn;
    public const string Notice = "Ranked: surrendering or timing out before turn 3 gives no loss points. Three early forfeits within 10 minutes pause ranked queueing for 2 minutes.";
}
