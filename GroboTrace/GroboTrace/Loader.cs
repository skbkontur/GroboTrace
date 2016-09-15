using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using RGiesecke.DllExport;

namespace GroboTrace
{
    public static class Loader
    {
        private static string profilerDirectory;

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (!args.Name.StartsWith("GrEmit,"))
                return null;
            if (File.Exists("GrEmit.dll"))
                return null;
            Debug.WriteLine("Asked to load GrEmit: " + args.Name);
            return Assembly.LoadFrom(Path.Combine(profilerDirectory, "GrEmit.dll"));
        }

        [DllExport]
        public static void SetProfilerPath([MarshalAs(UnmanagedType.LPWStr)] string profilerDirectory)
        {
            Debug.WriteLine("Profiler directory: " + profilerDirectory);

            Loader.profilerDirectory = profilerDirectory;

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }
    }
}