using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace GroboTrace
{
    public static class TracingAnalyzer
    {
        static TracingAnalyzer()
        {
            var groboTraceAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.FullName.StartsWith("GroboTrace.Core, "));
            if(groboTraceAssembly == null)
            {
                Debug.WriteLine("There is no GroboTrace.Core loaded into current AppDomain");
                getStatsDelegate = () =>
                                   new Stats
                                       {
                                           Tree = new MethodStatsNode(),
                                           List = new List<MethodStats>(),
                                           ElapsedTicks = 0
                                       };
                clearStatsDelegate = () => { };
            }
            else
            {
                Debug.WriteLine("GroboTrace.Core is loaded into current AppDomain");
                var tracingAnalyzerType = groboTraceAssembly.GetType("GroboTrace.Core.TracingAnalyzer");
                if(tracingAnalyzerType == null)
                    throw new InvalidOperationException("Unable to load type GroboTrace.Core.TracingAnalyzer");
                var getStatsMethod = tracingAnalyzerType.GetMethod("GetStats", BindingFlags.Static | BindingFlags.Public);
                if(getStatsMethod == null)
                    throw new InvalidOperationException("Missing method GroboTrace.Core.TracingAnalyzer.GetStats");
                var clearStatsMethod = tracingAnalyzerType.GetMethod("ClearStats", BindingFlags.Static | BindingFlags.Public);
                if(clearStatsMethod == null)
                    throw new InvalidOperationException("Missing method GroboTrace.Core.TracingAnalyzer.ClearStats");
                getStatsDelegate = () => (Stats)getStatsMethod.Invoke(null, new object[0]);
                clearStatsDelegate = () => clearStatsMethod.Invoke(null, new object[0]);
            }
        }

        public static Stats GetStats()
        {
            return getStatsDelegate();
        }

        public static void ClearStats()
        {
            clearStatsDelegate();
        }

        private static readonly Func<Stats> getStatsDelegate;
        private static readonly Action clearStatsDelegate;
    }
}