using System;
using System.Collections.Generic;
using System.Linq;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class FriendListCache
{
    public const int PageSize = 50;
    public const int MaxPages = 32;
    public readonly record struct PageKey(string Group, string Search, int Offset);
    public readonly record struct Read(int Generation, PageKey Key);
    private readonly Dictionary<PageKey, FriendPageDto> pages = [];
    private readonly LinkedList<PageKey> recent = [];
    private readonly Queue<PageKey> pending = [];
    private readonly HashSet<PageKey> queued = [];
    private int generation;
    public int Generation => generation;
    public bool HasPending => pending.Count > 0;
    public void Clear()
    {
        generation++; pages.Clear(); recent.Clear(); pending.Clear(); queued.Clear();
    }
    public FriendCountsDto? Counts(string search)
    {
        search = search.Trim();
        for (var node = recent.Last; node is not null; node = node.Previous)
            if (node.Value.Search == search) return pages[node.Value].Counts;
        Queue(new("online", search, 0)); return null;
    }
    public FriendEntryDto? Entry(string group, string search, int index)
    {
        var key = new PageKey(group, search.Trim(), index / PageSize * PageSize);
        if (!pages.TryGetValue(key, out var page)) { Queue(key); return null; }
        recent.Remove(key); recent.AddLast(key);
        return page.Entries.ElementAtOrDefault(index - key.Offset);
    }
    private void Queue(PageKey key)
    {
        if (pending.Count < 16 && queued.Add(key)) pending.Enqueue(key);
    }
    public Read? Take()
    {
        if (!pending.TryDequeue(out var key)) return null;
        return new(generation, key);
    }
    public bool Apply(Read read, FriendPageDto page)
    {
        if (read.Generation != generation) return false;
        queued.Remove(read.Key);
        if (page.Group != read.Key.Group || page.Search != read.Key.Search || page.Offset != read.Key.Offset) return false;
        pages[read.Key] = page; recent.Remove(read.Key); recent.AddLast(read.Key);
        while (recent.Count > MaxPages) { pages.Remove(recent.First!.Value); recent.RemoveFirst(); }
        return true;
    }
    public void Retry(Read read)
    {
        if (read.Generation != generation) return;
        queued.Remove(read.Key); Queue(read.Key);
    }
    public int CachedPages => pages.Count;
}
