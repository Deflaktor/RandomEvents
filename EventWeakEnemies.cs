using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RandomEvents
{
    public class EventWeakEnemies : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        // the director multiplies an elite tier's cost by the bias when it picks one, so a high bias makes elites too
        // expensive
        private const float NoElitesBias = 1000f;
        // the Simulacrum wave the event changed, and its elite bias and cheap skipping before
        private InfiniteTowerWaveController changedWave;
        private float originalEliteBias;
        private bool originalSkipSpawnIfTooCheap;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                // not for boss waves
                if ((infiniteTowerRun.waveIndex + 1) % 5 == 0)
                {
                    return false;
                }
            }
            // DroneUprising replaces the stage's monsters, which this event picks from
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("StrongEnemies", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("Mitosis", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("DroneUprising", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("LynxPups", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("BlightedCorpses", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("BulletHell", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "WeakEnemies";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_WEAK_ENEMIES");
        }
        public override string GetDescription()
        {
            return "Only weak enemies spawn.";
        }
        public override string GetConditionDescription()
        {
            return "Events \"StrongEnemies\", \"Mitosis\", \"DroneUprising\", \"LynxPups\", \"BlightedCorpses\" and \"BulletHell\" inactive.";
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
            // ModCompatibilityServerSideTweaks.SetOverridePowerBias(0f);
            changedWave = null;
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null && infiniteTowerRun.waveController.combatDirector != null)
            {
                changedWave = infiniteTowerRun.waveController;
                originalEliteBias = changedWave.combatDirector.eliteBias;
                changedWave.combatDirector.eliteBias = NoElitesBias;
                changedWave.combatDirector.monsterCredit = 0f;
                // The director skips cards too cheap for its credits while its deck has dearer ones, counting the elite
                // versions it can no longer afford. With only cheap monsters left, it would then never spend, its
                // credits would only grow, and nothing would spawn until the wave gives up.
                originalSkipSpawnIfTooCheap = changedWave.combatDirector.skipSpawnIfTooCheap;
                changedWave.combatDirector.skipSpawnIfTooCheap = false;
            }
            var costs = MonsterPool.GetChoices()
                .Where(choice => choice.weight > 0 && choice.value != null)
                .Select(choice => choice.value.spawnCard.directorCreditCost)
                .OrderBy(cost => cost)
                .ToList();
            var count = costs.Count;
            if(count > 2)
            {
                // float median = (count % 2 == 1) ? costs[count / 2] : (costs[(count / 2) - 1] + costs[count / 2]) / 2f;
                // with fewer than 4 costs there's nothing below the first quarter to average with, e.g. a family of 3 monsters
                int quarter = count / 4;
                float oneQuarterMedian = (count % 4 == 0 || quarter == 0) ? costs[quarter] : (costs[quarter - 1] + costs[quarter]) / 2f;
                MonsterPool.Filter(this, choice => choice.value != null && choice.value.spawnCard.directorCreditCost <= oneQuarterMedian);
            }
        }

        public override void Stop()
        {
            // ModCompatibilityServerSideTweaks.ResetOverridePowerBias();
            MonsterPool.Restore(this);
            // the wave keeps going after the event, so it goes back to normal; a later wave is unchanged anyway
            if (changedWave != null && changedWave.combatDirector != null)
            {
                changedWave.combatDirector.eliteBias = originalEliteBias;
                changedWave.combatDirector.skipSpawnIfTooCheap = originalSkipSpawnIfTooCheap;
            }
            changedWave = null;
        }
    }
}
