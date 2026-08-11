using System;
class Program {
    static void Main() {
        bool res = DateTime.TryParse("?", out var dt);
        Console.WriteLine($"Result: {res}, Date: {dt}");
    }
}
