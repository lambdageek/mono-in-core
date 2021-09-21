using Embedding.Mono;
public class SampleHost
{
    public static int Main(string[] args)
    {
        if (args.Length != 1) {
            Console.WriteLine("Usage: SampleHost APP_DIR");
            return 1;
        }

        string appDir = args[0];

        if (!Directory.Exists(appDir)) {
            Console.WriteLine("Directory not found: " + appDir);
            return 1;
        }

        // FIXME: on non-OSX, use libmonosgen-2.0.so
        string monoLibPath = Path.Combine(appDir, "libcoreclr.dylib");

        if (!File.Exists(monoLibPath)) {
            Console.WriteLine("File not found: " + monoLibPath);
            return 1;
        }

        string[] tpaList = GetTPAList(appDir);

        using var host = Embedding.Mono.HostedMono.Make(monoLibPath);
        (var vm, var rootDomain) = StartMono (host, tpaList);
        Console.WriteLine ("Mono started");
        RunSample (vm, rootDomain, appDir);
        Console.WriteLine ("Sample run complete"); 
        return 0;
    }

    public static string[] GetTPAList(string appDir) =>  Directory.GetFiles(appDir, "*.dll");

    public static (HostedMono.VM, HostedMono.MonoDomain) StartMono (HostedMono host, string[] tpaList)
    {
 
        var vm = host.GetVM();
        var props = new (string,string)[] {
            ("TRUSTED_PLATFORM_ASSEMBLIES", string.Join(Path.PathSeparator, tpaList)),
        };
        vm.Initialize(props);
        var rootDomain = vm.JitInitVersion("mono_in_core", Embedding.Mono.HostedMono.NormalVersion);
        return (vm, rootDomain);
    }
    public static void RunSample(HostedMono.VM vm, HostedMono.MonoDomain rootDomain, string appDir)
    {
        var assm = vm.OpenAssembly(rootDomain, Path.Combine(appDir, "InnerApp.dll"));
        vm.ExecuteAssembly(rootDomain, assm, new string[]{"hi"});
    }
}