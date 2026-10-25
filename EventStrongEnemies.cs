using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RandomEvents
{
    public class EventStrongEnemies : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        private const float WaveCreditsMultiplier = 2f;
        // the Simulacrum wave the event changed; its extra credits also buy more elites
        private InfiniteTowerWaveController changedWave;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // DroneUprising replaces the stage's monsters, which this event picks from
            return !Helper.IsSimulacrumBossWaveNext() && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("WeakEnemies", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("Mitosis", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("DroneUprising", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("LynxPups", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("BlightedCorpses", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("BulletHell", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "StrongEnemies";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_STRONG_ENEMIES");
        }
        public override string GetDescription()
        {
            return "Only strong enemies spawn.";
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave, and events \"WeakEnemies\", \"Mitosis\", \"DroneUprising\", \"LynxPups\", \"BlightedCorpses\" and \"BulletHell\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            
        }

        public override void Preload()
        {

        }

        public override void Hook()
        {

        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            //ModCompatibilityServerSideTweaks.SetOverridePowerBias(1f);
            changedWave = null;
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null && infiniteTowerRun.waveController.combatDirector != null)
            {
                changedWave = infiniteTowerRun.waveController;
                changedWave.creditsPerSecond *= WaveCreditsMultiplier;
            }
            var costs = MonsterPool.GetChoices()
                .Where(choice => choice.weight > 0 && choice.value != null)
                .Select(choice => choice.value.spawnCard.directorCreditCost)
                .OrderBy(cost => cost)
                .ToList();
            var count = costs.Count;
            if (count > 2)
            {
                float median = (count % 2 == 1) ? costs[count / 2] : (costs[(count / 2) - 1] + costs[count / 2]) / 2f;
                // float twoThirdsMedian = ((2*count) % 3 == 0) ? costs[2 * count / 3] : (costs[(2*count / 3) - 1] + costs[2*count / 3]) / 2f;
                MonsterPool.Filter(this, choice => choice.value != null && choice.value.spawnCard.directorCreditCost >= median);
            }
        }

        public override void Stop()
        {
            //ModCompatibilityServerSideTweaks.ResetOverridePowerBias();
            MonsterPool.Restore(this);
            // the wave keeps going after the event, so it goes back to normal; a later wave is unchanged anyway
            if (changedWave != null && changedWave.combatDirector != null)
            {
                changedWave.creditsPerSecond /= WaveCreditsMultiplier;
            }
            changedWave = null;
        }
    }
}
