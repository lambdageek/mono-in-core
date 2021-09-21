bool isMono = typeof(object).Assembly.GetType("Mono.RuntimeStructs") != null;
Console.WriteLine($"Hello World {(isMono ? "from Mono!" : "from CoreCLR!")}");

