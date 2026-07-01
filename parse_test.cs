using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

class Program {
    static void Main() {
        var path = ""C:\Users\ito\Desktop\manami.json"";
        if (!File.Exists(path)) { Console.WriteLine(""Not found""); return; }
        
        using (var stream = File.OpenRead(path))
        using (var doc = JsonDocument.Parse(stream)) {
            var data = doc.RootElement.GetProperty(""data"");
            var count = 0;
            foreach (var item in data.EnumerateArray()) {
                string title = item.GetProperty(""title"").GetString();
                int anidbId = 0;
                foreach (var source in item.GetProperty(""sources"").EnumerateArray()) {
                    string url = source.GetString();
                    if (url.StartsWith(""https://anidb.net/anime/"")) {
                        int.TryParse(url.Substring(""https://anidb.net/anime/"".Length), out anidbId);
                        break;
                    }
                }
                if (anidbId != 0 && count < 5) {
                    Console.WriteLine($""Title: {title}, AniDB ID: {anidbId}"");
                    count++;
                }
            }
        }
    }
}
