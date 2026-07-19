using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

class Program {
    static string BuildAnimeXml(int id, string title, List<Tuple<int, string>> relations, int episodes = 12)
    {
        var relatedAnimeXml = string.Join("
", relations.Select(r => $"<anime id="{r.Item1}" type="{r.Item2}">Related</anime>"));

        var episodesXml = "";
        for (var i = 1; i <= episodes; i++)
        {
            episodesXml += $"<episode><epno type="1">{i}</epno><length>25</length><title xml:lang="en">Episode {i}</title></episode>
";
        }

        return $@"<?xml version="1.0" encoding="UTF-8"?>
<anime id="{id}">
  <titles>
    <title xml:lang="en" type="main">{title}</title>
  </titles>
  <type>TV Series</type>
  <relatedanime>
    {relatedAnimeXml}
  </relatedanime>
  <episodes>
    {episodesXml}
  </episodes>
</anime>";
    }

    static void Main() {
        var xml = BuildAnimeXml(1, "Season 1", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel"), Tuple.Create(3, "Sequel") });
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var related = doc.Root?.Element(ns + "relatedanime");
        if (related == null) {
            Console.WriteLine("related is null");
            return;
        }

        var results = new List<int>();
        foreach (var anime in related.Elements(ns + "anime"))
        {
            var type = (string)anime.Attribute("type");
            if (string.Equals(type, "Sequel", StringComparison.OrdinalIgnoreCase))
            {
                var idStr = (string)anime.Attribute("id");
                if (int.TryParse(idStr, out var id) && id > 0)
                {
                    results.Add(id);
                }
            }
        }
        Console.WriteLine("Sequels found: " + results.Count);
    }
}
