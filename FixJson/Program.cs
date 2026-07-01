using System;
using System.IO;

class Program {
    static void Main() {
        var path = @"C:\Users\ito\Desktop\Anidarr\Sonarr-5-develop\src\NzbDrone.Core\Localization\Core\en.json";
        var content = File.ReadAllText(path);
        
        var strToFind1 = "{\n    \"Mal\":  \"MyAnimeList\",\r\n    \"Tvdb\":  \"TheTVDB\",\r\n    \"AniDb\":  \"AniDB\",\r\n    \"AniList\":  \"AniList\",\r\n    \"Simkl\":  \"Simkl\",";
        var strToFind2 = "{\r\n    \"Mal\":  \"MyAnimeList\",\r\n    \"Tvdb\":  \"TheTVDB\",\r\n    \"AniDb\":  \"AniDB\",\r\n    \"AniList\":  \"AniList\",\r\n    \"Simkl\":  \"Simkl\",";
        var strToFind3 = "{\n    \"Mal\":  \"MyAnimeList\",\n    \"Tvdb\":  \"TheTVDB\",\n    \"AniDb\":  \"AniDB\",\n    \"AniList\":  \"AniList\",\n    \"Simkl\":  \"Simkl\",";
        
        content = content.Replace(strToFind1, "{");
        content = content.Replace(strToFind2, "{");
        content = content.Replace(strToFind3, "{");
        
        var insertStr = "{\r\n  \"Mal\": \"MyAnimeList\",\r\n  \"Tvdb\": \"TheTVDB\",\r\n  \"AniDb\": \"AniDB\",\r\n  \"AniList\": \"AniList\",\r\n  \"Simkl\": \"Simkl\",";
        int idx = content.IndexOf('{');
        if (idx >= 0) {
            content = content.Substring(0, idx) + insertStr + content.Substring(idx + 1);
        }
        
        File.WriteAllText(path, content);
        Console.WriteLine("Done");
    }
}
