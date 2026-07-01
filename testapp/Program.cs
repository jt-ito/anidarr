using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var url = "http://anidb.net/api/anime-titles.dat.gz";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Sonarr/5.0");
        var response = await client.GetAsync(url);
        
        using var fs = new FileStream("anime-titles.dat.gz", FileMode.Create);
        await response.Content.CopyToAsync(fs);
        fs.Close();
        
        using var inFs = new FileStream("anime-titles.dat.gz", FileMode.Open);
        using var gzip = new GZipStream(inFs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        
        for (int i = 0; i < 20; i++)
        {
            Console.WriteLine(await reader.ReadLineAsync());
        }
    }
}
