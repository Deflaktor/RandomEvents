using RoR2;
using System;
using System.Runtime.CompilerServices;

namespace RandomEvents
{
    public static class ModCompatibilityShareSuite
    {
        private static bool? _enabled;

        public static bool enabled
        {
            get
            {
                if (_enabled == null)
                {
                    _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite");
                }
                return (bool)_enabled;
            }
        }

        // a pickup that a handler returns false for isn't shared
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddPickupEventHandler(Func<GenericPickupController, CharacterBody, bool> f)
        {
            ShareSuite.ItemSharingHooks.AdditionalPickupValidityChecks += f;
        }
    }
}
