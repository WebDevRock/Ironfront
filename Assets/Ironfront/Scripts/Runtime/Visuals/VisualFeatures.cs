using System;
using System.Linq;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Switches for optional presentation features, read once from the command line so a single build can be
    /// profiled with features on and off (Milestone 006 performance investigation):
    /// <c>-fxNoTreads</c>, <c>-fxNoTrails</c>, <c>-fxNoWrecks</c>, <c>-fxNoParticles</c>, <c>-fxNoTilt</c>, and
    /// (Milestone 007, profiling only: it changes gameplay) <c>-fxNoFog</c>. Everything defaults to on.
    /// </summary>
    public static class VisualFeatures
    {
        public static bool TreadScroll { get; private set; } = true;
        public static bool Trails { get; private set; } = true;
        public static bool Wrecks { get; private set; } = true;
        public static bool Particles { get; private set; } = true;
        public static bool HullTilt { get; private set; } = true;
        public static bool Fog { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Read()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { args = new string[0]; }
            TreadScroll = !args.Contains("-fxNoTreads");
            Trails = !args.Contains("-fxNoTrails");
            Wrecks = !args.Contains("-fxNoWrecks");
            Particles = !args.Contains("-fxNoParticles");
            HullTilt = !args.Contains("-fxNoTilt");
            Fog = !args.Contains("-fxNoFog");
        }

        /// <summary>Summary for logs, e.g. "treads trails wrecks particles tilt".</summary>
        public static string Describe() =>
            $"treads={TreadScroll} trails={Trails} wrecks={Wrecks} particles={Particles} tilt={HullTilt} fog={Fog}";
    }
}
