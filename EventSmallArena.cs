using BepInEx.Configuration;
using HG;
using RoR2;
using RoR2.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using static RandomEvents.EventHaste;
using static RoR2.HoldoutZoneController;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventSmallArena : AbstractEvent
    {

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                // not for super boss waves
                if ((infiniteTowerRun.waveIndex + 1) % 50 == 0)
                {
                    return false;
                }
                var safeWardController = infiniteTowerRun.safeWardController;
                return safeWardController != null;
            }
            return false;
        }
        public override string GetEventConfigName()
        {
            return "SmallArena";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SMALL_ARENA");
        }
        public override string GetDescription()
        {
            return "Halves the battle zone radius but makes the wave also easier.";
        }
        public override string GetConditionDescription()
        {
            return "Simulacrum mode only";
        }
        protected override void AddConfig(ConfigFile config)
        {

        }

        public override void Preload()
        {
        }

        public override void Hook()
        {
            On.RoR2.InfiniteTowerWaveController.FixedUpdate += InfiniteTowerWaveController_FixedUpdate;
        }

        private void InfiniteTowerWaveController_FixedUpdate(On.RoR2.InfiniteTowerWaveController.orig_FixedUpdate orig, InfiniteTowerWaveController self)
        {
            orig(self);
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive()) {
                if (self.Network_zoneRadiusPercentage >= 1f)
                {
                    self.Network_zoneRadiusPercentage *= 0.5f;
                }
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                infiniteTowerRun.waveController.totalWaveCredits *= 0.65f;
                infiniteTowerRun.waveController.creditsPerSecond *= 0.65f;
                //if(infiniteTowerRun.safeWardController.wardStateMachine.state is EntityStates.InfiniteTowerSafeWard.Active state)
                //{
                //    state.radius *= 0.5f;
                //}
                //else
                //{
                //    Log.LogError("Could not get EntityStateMachine of safeWardController");
                //}
            }
        }

        public override void Stop()
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                //if (infiniteTowerRun.safeWardController.wardStateMachine.state is EntityStates.InfiniteTowerSafeWard.Active state)
                //{
                //    state.radius *= 2f;
                //}
                //else
                //{
                //    Log.LogError("Could not get EntityStateMachine of safeWardController");
                //}
            }
        }
    }
}
