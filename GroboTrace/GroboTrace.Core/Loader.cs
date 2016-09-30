using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using RGiesecke.DllExport;

namespace GroboTrace.Core
{
    // Resolves GrEmit.dll - seems that this can only be done on .NET level
    public static class Loader
    {
        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            if(!args.Name.StartsWith("GrEmit,"))
                return null;
            if(File.Exists("GrEmit.dll"))
                return null;
            Debug.WriteLine("Asked to load GrEmit: " + args.Name);
            return Assembly.LoadFrom(Path.Combine(profilerDirectory, "GrEmit.dll"));
        }

        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static void SetProfilerPath([MarshalAs(UnmanagedType.LPWStr)] string profilerDirectory)
        {
            Debug.WriteLine("Profiler directory: " + profilerDirectory);

            Loader.profilerDirectory = profilerDirectory;

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private static string profilerDirectory;
    }
}