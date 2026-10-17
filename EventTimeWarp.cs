using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventTimeWarp : AbstractEvent
    {
        // Slow reductions add up: Chronobauble's Slow60 alone leaves 1 / 1.6 = 62.5% speed, all three 1 / 2.9 = 34.5%.
        // Slow60 also shows Chronobauble's clock effect.
        private static readonly BuffDef[] slows = { RoR2Content.Buffs.Slow60, RoR2Content.Buffs.Slow80, RoR2Content.Buffs.Slow50 };
        private readonly BuffTracker buffs = new BuffTracker();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return true;
        }
        public override string GetEventConfigName()
        {
            return "TimeWarp";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_TIME_WARP");
        }
        public override string GetDescription()
        {
            return "Slows all enemies to about a third of their speed, almost twice as much as Chronobauble.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive())
            {
                Slow(body);
            }
        }

        private void Slow(CharacterBody body)
        {
            if (body.teamComponent.teamIndex == TeamIndex.Player || buffs.Has(body, slows[0]))
            {
                return;
            }
            foreach (var slow in slows)
            {
                buffs.Add(body, slow);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    Slow(body);
                }
            }
        }

        public override void Stop()
        {
            buffs.RemoveAll();
        }
    }
}
