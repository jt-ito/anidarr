using System;
using System.Linq;
using NzbDrone.Core.Parser;
using NzbDrone.Common.Extensions;

class Program {
    static void Main() {
        string t1 = "My Classmate's a Sexy Actress, and Now We Live Together?!";
        string t2 = "My Classmate's a Sexy Actress";
        string t3 = "同じゼミの染谷さんがセクシー女優だった話。";
        Console.WriteLine($"t1 -> {t1.CleanSeriesTitle()}");
        Console.WriteLine($"t2 -> {t2.CleanSeriesTitle()}");
        Console.WriteLine($"t3 -> {t3.CleanSeriesTitle()}");
    }
}
