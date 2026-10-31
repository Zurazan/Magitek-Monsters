using System;
using System.IO;

namespace MagitekMonsters.Services;

public static class PokemonSpriteSource
{
    public const string Style = "showdown-modern-home-v1";
    public const string CacheFolder = "showdown-modern-v1";
    public const int MaximumAnimationBytes = 32 * 1024 * 1024;

    public static string AnimationUrl(string id, bool shiny)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100)
            throw new InvalidDataException("Invalid sprite ID.");
        foreach (var character in id)
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                throw new InvalidDataException("Invalid sprite ID.");
        return $"https://play.pokemonshowdown.com/sprites/{(shiny ? "ani-shiny" : "ani")}/{id}.gif";
    }
}
