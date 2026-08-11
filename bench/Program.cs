using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;

class Program
{
    static void Main()
    {
        int seriesCount = 16845;
        // ~2.6 titles per series on average (CleanTitle + synonyms)
        var flatTitles = new List<string>(seriesCount * 3);
        
        Random r = new Random(42);
        for (int i = 0; i < seriesCount; i++)
        {
            // main title
            flatTitles.Add(new string('a', r.Next(3, 40)));
            // synonym 1
            flatTitles.Add(new string('b', r.Next(3, 40)));
            if (i % 2 == 0) {
                // synonym 2
                flatTitles.Add(new string('c', r.Next(3, 40)));
            }
        }
        
        // ensure our target exists
        flatTitles.Add("naruto shippuden");
        
        int totalTitles = flatTitles.Count;
        Console.WriteLine($"Generated {seriesCount} series yielding {totalTitles} flat title strings.");
        
        string query = "naruto shipuden";
        string cleanQuery = query.CleanForSearch();

        // Benchmark Substring (Contains)
        var sw = Stopwatch.StartNew();
        int count1 = 0;
        foreach (var t in flatTitles)
        {
            if (t.Contains(cleanQuery)) count1++;
        }
        sw.Stop();
        Console.WriteLine($"Substring (Contains) time: {sw.ElapsedMilliseconds} ms. Matches: {count1}");

        // Benchmark Levenshtein
        sw.Restart();
        int count2 = 0;
        foreach (var t in flatTitles)
        {
            int dist = t.LevenshteinDistance(cleanQuery);
            int maxLen = Math.Max(t.Length, cleanQuery.Length);
            if (dist <= Math.Max(1, Math.Floor(maxLen * 0.2))) count2++;
        }
        sw.Stop();
        Console.WriteLine($"Levenshtein (Always-on) time: {sw.ElapsedMilliseconds} ms. Matches: {count2}");

        // Benchmark Early-Exit (Length filter)
        sw.Restart();
        int count3 = 0;
        foreach (var t in flatTitles)
        {
            int maxLen = Math.Max(t.Length, cleanQuery.Length);
            int allowed = (int)Math.Max(1, Math.Floor(maxLen * 0.2));
            
            if (Math.Abs(t.Length - cleanQuery.Length) > allowed) continue;
            
            int dist = t.LevenshteinDistance(cleanQuery);
            if (dist <= allowed) count3++;
        }
        sw.Stop();
        Console.WriteLine($"Levenshtein with length pre-filter time: {sw.ElapsedMilliseconds} ms. Matches: {count3}");
    }
}
