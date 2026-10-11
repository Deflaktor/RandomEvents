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
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("StrongEnemies", StringComparison.InvariantCultureIgnoreCase));
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
            return "Event \"StrongEnemies\" inactive.";
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
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                infiniteTowerRun.waveController.combatDirector.eliteBias = 0f;
                infiniteTowerRun.waveController.combatDirector.monsterCredit = 0f;
            }
            var costs = ClassicStageInfo.instance.monsterSelection.choices
                .Where(choice => choice.weight > 0 && choice.value != null)
                .Select(choice => choice.value.spawnCard.directorCreditCost)
                .OrderBy(cost => cost)
                .ToList();
            var count = costs.Count;
            if(count > 2)
            {
                // float median = (count % 2 == 1) ? costs[count / 2] : (costs[(count / 2) - 1] + costs[count / 2]) / 2f;
                float oneQuarterMedian = (count % 4 == 0) ? costs[count / 4] : (costs[(count / 4) - 1] + costs[count / 4]) / 2f;
                var atLeastOneChoice = false;
                var filteredWeightedSelection = new WeightedSelection<DirectorCard>();
                foreach (var choice in ClassicStageInfo.instance.monsterSelection.choices)
                {
                    if(choice.value != null && choice.value.spawnCard.directorCreditCost <= oneQuarterMedian)
                    {
                        atLeastOneChoice = true;
                        filteredWeightedSelection.AddChoice(choice);
                    }
                }
                if (atLeastOneChoice)
                {
                    ClassicStageInfo.instance.monsterSelection = filteredWeightedSelection;
                }
            }
        }

        public override void Stop()
        {
            // ModCompatibilityServerSideTweaks.ResetOverridePowerBias();
            if (ClassicStageInfo.instance != null)
            {
                ClassicStageInfo.instance.RebuildCards();
            }
        }
    }
}
