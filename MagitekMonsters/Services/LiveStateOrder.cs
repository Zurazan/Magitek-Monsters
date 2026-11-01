using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public static class LiveStateOrder
{
    public static bool IsOlder(BattleSnapshotDto next, BattleSnapshotDto? current) => current is not null && current.Id == next.Id
        && (current.Revision.HasValue && (!next.Revision.HasValue || next.Revision < current.Revision)
            || current.Status is "won" or "lost" or "draw" or "error" && next.Status == "active");
}
