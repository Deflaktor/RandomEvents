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
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("WeakEnemies", StringComparison.InvariantCultureIgnoreCase));
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
            return "Event \"WeakEnemies\" inactive.";
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
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                infiniteTowerRun.waveController.combatDirector.eliteBias = 3f;
                infiniteTowerRun.waveController.creditsPerSecond *= 2.0f;
            }
            var costs = ClassicStageInfo.instance.monsterSelection.choices
                .Where(choice => choice.weight > 0 && choice.value != null)
                .Select(choice => choice.value.spawnCard.directorCreditCost)
                .OrderBy(cost => cost)
                .ToList();
            var count = costs.Count;
            if (count > 2)
            {
                float median = (count % 2 == 1) ? costs[count / 2] : (costs[(count / 2) - 1] + costs[count / 2]) / 2f;
                // float twoThirdsMedian = ((2*count) % 3 == 0) ? costs[2 * count / 3] : (costs[(2*count / 3) - 1] + costs[2*count / 3]) / 2f;
                var atLeastOneChoice = false;
                var filteredWeightedSelection = new WeightedSelection<DirectorCard>();
                foreach (var choice in ClassicStageInfo.instance.monsterSelection.choices)
                {
                    if (choice.value != null && choice.value.spawnCard.directorCreditCost >= median)
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
            //ModCompatibilityServerSideTweaks.ResetOverridePowerBias();
            if (ClassicStageInfo.instance != null)
            {
                ClassicStageInfo.instance.RebuildCards();
            }
        }
    }
}
