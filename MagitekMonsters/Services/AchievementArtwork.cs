using System;
using System.Collections.Generic;

namespace MagitekMonsters.Services;

public static class AchievementArtwork
{
    private static readonly Dictionary<string, int> Badges = BuildBadges();
    private static readonly Dictionary<string, string> Trials = new(StringComparer.Ordinal)
    {
        ["verdant"] = "normalium", ["melemele"] = "fightinium", ["brooklet"] = "waterium", ["wela"] = "firium",
        ["lush"] = "grassium", ["akala"] = "rockium", ["hokulani"] = "electrium", ["megamart"] = "ghostium",
        ["ulaula"] = "darkinium", ["mina"] = "fairium", ["poni"] = "groundium"
    };

    public static string RelativePath(string id)
    {
        if (Badges.TryGetValue(id, out var number)) return "badges/" + number + ".png";
        if (id.StartsWith("league:alola:", StringComparison.Ordinal) && Trials.TryGetValue(id[13..], out var crystal))
            return "items/" + crystal + "-z--bag.png";
        var ribbon = id switch
        {
            "champion:hoenn" => "championg6hoenn", "champion:sinnoh" => "championsinnoh", "champion:kalos" => "championkalos",
            "champion:alola" => "championalola", "champion:galar" => "championgalar", "champion:paldea" => "championpaldea",
            "champion:blueberry" => "championnational", "champion:kanto" or "champion:johto" or "champion:unova" => "championg3",
            "partner" => "bestfriends",
            "frontier:accessible-7" => "ability", "frontier:accessible-14" => "abilitygreat", "frontier:accessible-21" => "abilitydouble",
            "frontier:accessible-28" => "abilitymulti", "frontier:accessible-49" => "abilitypair", "frontier:accessible-70" => "abilityworld",
            "frontier:standard-7" => "winning", "frontier:standard-14" => "victory", "frontier:standard-21" => "battlerskillful",
            "frontier:standard-28" => "battlerexpert", "frontier:standard-35" => "battletreegreat", "frontier:standard-49" => "battletreemaster",
            "frontier:standard-70" => "towermaster", "frontier:lifetime-25" => "record", "frontier:lifetime-50" => "training",
            "frontier:lifetime-100" => "legend", "frontier:lifetime-200" => "masterrank", "frontier:rental-10" => "abilitypair", "frontier:owned-10" => "partner",
            _ when id.StartsWith("collection:v1-shiny-", StringComparison.Ordinal) => "twinklingstar",
            _ when id.StartsWith("collection:v1-family-", StringComparison.Ordinal) => "bestfriends",
            _ when id.StartsWith("collection:v1-generation-", StringComparison.Ordinal) => "national",
            _ when id.StartsWith("collection:v1-type-", StringComparison.Ordinal) => id.EndsWith("-50", StringComparison.Ordinal) ? "g4smartmaster"
                : id.EndsWith("-25", StringComparison.Ordinal) ? "g4smartultra" : "g4smartgreat",
            _ when id.StartsWith("collection:v1-species-", StringComparison.Ordinal) => "national",
            _ => "classic"
        };
        return "ribbons/ribbon" + ribbon + ".png";
    }

    private static Dictionary<string, int> BuildBadges()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        Add("kanto", 1, "boulder cascade thunder rainbow soul marsh volcano earth");
        Add("johto", 9, "zephyr hive plain fog storm mineral glacier rising");
        Add("hoenn", 17, "stone knuckle dynamo heat balance feather mind rain");
        Add("sinnoh", 25, "coal forest cobble fen relic mine icicle beacon");
        Add("unova", 33, "trio basic toxic insect bolt quake jet freeze legend wave");
        Add("kalos", 43, "bug cliff rumble plant voltage fairy psychic iceberg");
        Add("galar", 51, "grass water fire fighting ghost fairy rock ice dark dragon");
        Add("paldea", 70, "cortondo artazon levincia cascarrafa medali montenevera alfornada glaseado");
        return result;
        void Add(string region, int first, string names)
        {
            foreach (var name in names.Split(' ')) result.Add("league:" + region + ":" + name, first++);
        }
    }
}
