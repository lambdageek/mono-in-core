using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Embedding.Mono;

public class HostedMono : IDisposable
{
    public const string MonoRuntime = "monosgen-2.0";
    /// The version to pass to mono_jit_init_version for normal operation.
    public const string NormalVersion = "v4.0.30319";

    private HostedMono()
    {
    }

    public static HostedMono Make(string libMonoPath)
    {
        if (!File.Exists(libMonoPath))
            throw new FileNotFoundException("libMonoPath");

        if (Coordinator.TryAddRef(libMonoPath, out var host)) {
            return host;
        }    
        throw new Exception("Already loaded mono from another path");
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize (this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) {
            Coordinator.DecRef();
        }
    }

    ~HostedMono() => Dispose(disposing: false);

    internal struct State {
        public bool resolverInstalled;
        public bool resolverActive;
        public IntPtr nativeLibrary;
    };

    internal static State HostState;

    static void SetupMonoHost(string libMonoPath)
    {
        HostState.nativeLibrary = NativeLibrary.Load(libMonoPath);
        if (!HostState.resolverInstalled) {
            Console.Error.WriteLine($"Setting up resolver for {MonoRuntime} => {libMonoPath}");
            NativeLibrary.SetDllImportResolver(typeof(HostedMono).Assembly, MonoPinvokeResolver);
            HostState.resolverInstalled = true;
        }
        HostState.resolverActive = true;
    }

    static IntPtr MonoPinvokeResolver (string libraryName, System.Reflection.Assembly assembly, System.Runtime.InteropServices.DllImportSearchPath? searchPath) {
        if (!HostState.resolverActive)
            return IntPtr.Zero;
        if (libraryName != MonoRuntime)
            return IntPtr.Zero;
            return HostState.nativeLibrary;        
    }

    static void TearDownMonoHost()
    {
        NativeLibrary.Free(HostState.nativeLibrary);
        HostState.nativeLibrary = IntPtr.Zero;
        HostState.resolverActive = false;
    }

    internal static class Coordinator
    {
        private static int refcount;
        private static string loadedPath = string.Empty;
        private static readonly object lockobj = new ();
        
        public static bool TryAddRef (string libMonoPath, [NotNullWhen(true)] out HostedMono? host)
        {
            lock (lockobj) {
                if (refcount > 0 && libMonoPath != loadedPath) {
                    host = null;
                    return false;
                }
                refcount++;
                if (refcount == 1) {
                    loadedPath = libMonoPath;
                    SetupMonoHost(libMonoPath);
                }
                host = new HostedMono();
                return true;
            }
        }

        public static void DecRef()
        {
            lock (lockobj) {
                refcount--;
                if (refcount == 0) {
                    TearDownMonoHost();
                }
            }
        }

    }

    internal static class EntryPoint {
        [DllImport(MonoRuntime, CallingConvention = CallingConvention.Cdecl)]
        unsafe internal extern static void monovm_initialize(uint nprops, byte **propKeys, byte**propValues);

        [DllImport(MonoRuntime, CallingConvention = CallingConvention.Cdecl)]
        unsafe internal extern static IntPtr mono_jit_init_version(byte* name, byte* version);

        [DllImport(MonoRuntime, CallingConvention = CallingConvention.Cdecl)]
        unsafe internal extern static IntPtr mono_assembly_open (byte* assembly_name, int* error_code);

        [DllImport(MonoRuntime, CallingConvention = CallingConvention.Cdecl)]
        unsafe internal extern static int mono_jit_exec(IntPtr domain, IntPtr assembly, int argc, byte** argv);

    }

    public struct MonoDomain {
        public IntPtr Handle;
    }

    public struct MonoAssembly {
        public IntPtr Handle;
    }

    #pragma warning disable CA1822
    public VM GetVM() => new ();
    #pragma warning restore CA1822

    public class VM {
        internal VM() {}
        public void Initialize((string, string)[] properties) {
            uint count = (uint)properties.Length;
            unsafe {
                var keys = new byte*[count];
                var values = new byte*[count];
                try {
                    for (var i = 0; i < count; i++) {
                        keys[i] = (byte*)Marshal.StringToHGlobalAnsi(properties[i].Item1);
                        values[i] = (byte*)Marshal.StringToHGlobalAnsi(properties[i].Item2);
                    }
                    fixed (byte** keysPtr = keys) {
                        fixed (byte** valuesPtr = values) {
                            EntryPoint.monovm_initialize(count, keysPtr, valuesPtr);
                        }
                    }
                } finally {
                    for (var i = 0; i < count; i++) {
                        Marshal.FreeHGlobal((IntPtr)keys[i]);
                        Marshal.FreeHGlobal((IntPtr)values[i]);
                    }
                }
            }
        }

        public MonoDomain JitInitVersion (string name, string version) {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            byte[] versionBytes = System.Text.Encoding.UTF8.GetBytes(version);
            unsafe {
                fixed (byte* namePtr = nameBytes) {
                    fixed (byte* versionPtr = versionBytes) {
                        return new MonoDomain {
                            Handle = EntryPoint.mono_jit_init_version(namePtr, versionPtr)
                        };
                    }
                }
            }
        }

        public MonoAssembly OpenAssembly (MonoDomain domain, string assemblyName) {
            byte[] assemblyNameBytes = System.Text.Encoding.UTF8.GetBytes(assemblyName);
            unsafe {
                fixed (byte* assemblyNamePtr = assemblyNameBytes) {
                    int status;
                    var assembly = new MonoAssembly {
                        Handle = EntryPoint.mono_assembly_open(assemblyNamePtr, &status)
                    };
                    if (status != 0 || assembly.Handle == IntPtr.Zero) {
                        throw new Exception($"Failed to load assembly {assemblyName}");
                    }
                    return assembly;
                }
            }
        }

        public int ExecuteAssembly (MonoDomain domain, MonoAssembly assembly, string[] argv) {
            var argc = (uint)argv.Length;
            unsafe {
                var argvBytes = new byte*[argc];
                try {
                    for (var i = 0; i < argc; i++) {
                        argvBytes[i] = (byte*)Marshal.StringToHGlobalAnsi(argv[i]);
                    }
                    fixed (byte** argvPtr = argvBytes) {
                        return EntryPoint.mono_jit_exec(domain.Handle, assembly.Handle, (int)argc, argvPtr);
                    }
                } finally {
                    for (var i = 0; i < argc; i++) {
                        Marshal.FreeHGlobal((IntPtr)argvBytes[i]);
                    }
                }
            }
        }
    }

}
