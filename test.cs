using System;

class Program
{
    static void Main()
    {
        string title1 = "K-On".ToLowerInvariant(); // length 4
        string title2 = "K-0n".ToLowerInvariant(); // length 4 (typo)
        
        // CleanForSearch roughly leaves letters and digits
        string clean1 = "kon"; // len 3
        string clean2 = "k0n"; // len 3
        
        int dist = 1; // 1 char diff
        int maxLen = Math.Max(clean1.Length, clean2.Length); // 3
        
        bool isMatch = dist <= Math.Floor(maxLen * 0.2);
        
        Console.WriteLine($"maxLen: {maxLen}, allowed threshold: {Math.Floor(maxLen * 0.2)}, dist: {dist}, isMatch: {isMatch}");
        
        string t1 = "test"; // length 4
        string t2 = "tesx"; // length 4
        int m = 4;
        Console.WriteLine($"Len 4: threshold={Math.Floor(m * 0.2)}, dist=1, match={1 <= Math.Floor(m * 0.2)}");
    }
}
