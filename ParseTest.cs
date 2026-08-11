using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Main()
    {
        var SceneTitles = new List<string> {
            "My Classmate's a Sexy Actress, and Now We Live Together?!",
            "Onaji Zemi no Someya-san ga Sexy Joyuu Datta Hanashi.",
            "A Story about How Someya-san, a Girl from My College Seminar, Turned out to Be an AV Actress.",
            "同じゼミの染谷さんがセクシー女優だった話。"
        };
        var CleanSceneTitles = new List<string> {
            "My Classmates a Sexy Actress and Now We Live Together",
            "Onaji Zemi no Someya san ga Sexy Joyuu Datta Hanashi",
            "A Story about How Someya san a Girl from My College Seminar Turned out to Be an AV Actress",
            "同じゼミの染谷さんがセクシー女優だった話"
        };
        
        var AllSceneTitles = SceneTitles.Concat(CleanSceneTitles).Distinct().ToList();
        
        Console.WriteLine(AllSceneTitles.Count);
        foreach(var t in AllSceneTitles)
        {
            Console.WriteLine(t);
        }
    }
}
