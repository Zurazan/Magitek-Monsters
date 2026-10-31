namespace MagitekMonsters.Services;

public sealed class BattleLogFollow
{
    private string key = string.Empty;
    private float previousScroll;
    public bool Following { get; set; } = true;

    public bool Begin(string battle)
    {
        if (key == battle) return false;
        key = battle; previousScroll = 0; Following = true;
        return true;
    }

    public void Observe(float scroll, float maximum, bool hovered, float wheel, bool dragging)
    {
        if (Following && maximum > 0 && hovered && (wheel > 0 || dragging && scroll < previousScroll - 1)) Following = false;
        previousScroll = scroll;
    }
}
