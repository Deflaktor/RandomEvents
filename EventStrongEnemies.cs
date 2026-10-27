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
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterPool | EventTags.MonsterKinds;
        // SwarmQueen brings weak monsters along
        public override EventTags ExcludedTags => EventTags.MonsterKinds | EventTags.ExtraMonsters;
        private const float WaveCreditsMultiplier = 2f;
        // the Simulacrum wave the event changed; its extra credits also buy more elites
        private InfiniteTowerWaveController changedWave;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return !Helper.IsSimulacrumBossWaveNext();
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
        public override string GetPopupDescription()
        {
            return "Only strong monsters spawn.";
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave.";
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
