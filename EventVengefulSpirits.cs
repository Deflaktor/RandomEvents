using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventVengefulSpirits : AbstractEvent
    {
        // the monsters given a Vengeful Spirit, whose spirit or Dormant Spirit is taken back when the event ends
        private readonly List<CharacterMaster> spirited = new List<CharacterMaster>();
        private ConfigEntry<float> SpawnRate;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Vengeful Spirit comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4);
        }
        public override string GetEventConfigName()
        {
            return "VengefulSpirits";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_VENGEFUL_SPIRITS");
        }
        public override string GetDescription()
        {
            return "All monsters get a Vengeful Spirit: a spirit that takes half of their damage. Killing the spirit makes its monster vengeful, with more attack speed and shorter cooldowns. Half as many monsters spawn. The items are removed when the event ends.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            SpawnRate = config.Bind(GetEventConfigName(), "Spawn Rate", 0.5f, "How many monsters spawn during the event, relative to normal.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.CombatDirector.DirectorMoneyWave.Update += DirectorMoneyWave_Update;
        }

        // fewer monsters, since each has a spirit: the directors earn less to spend on them
        private float DirectorMoneyWave_Update(On.RoR2.CombatDirector.DirectorMoneyWave.orig_Update orig, CombatDirector.DirectorMoneyWave self, float deltaTime, float difficultyCoefficient)
        {
            var credits = orig(self, deltaTime, difficultyCoefficient);
            return NetworkServer.active && IsActive() ? credits * Mathf.Max(0f, SpawnRate.Value) : credits;
        }

        // monsters spawning during the event get one too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveSpirit(body);
            }
        }

        // A real item rather than a temporary one: the game can only turn a real Vengeful Spirit into a Dormant
        // Spirit when its spirit dies, and a temporary one would summon a new spirit forever instead.
        private void GiveSpirit(CharacterBody body)
        {
            var master = body != null ? body.master : null;
            if (master == null || master.inventory == null || spirited.Contains(master)
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral
                // the spirits themselves, which would summon spirits in turn
                || body.bodyIndex == BodyCatalog.FindBodyIndex("VengefulSpiritFollowerBody")
                || master.inventory.GetItemCountEffective(DLC4Content.Items.VengefulSpirit) > 0
                || master.inventory.GetItemCountEffective(DLC4Content.Items.VengefulSpiritRageMode) > 0)
            {
                return;
            }
            master.inventory.GiveItemPermanent(DLC4Content.Items.VengefulSpirit);
            spirited.Add(master);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            spirited.Clear();
            // Simulacrum's waves get their credits from the wave instead
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null)
            {
                var spawnRate = Mathf.Max(0f, SpawnRate.Value);
                infiniteTowerRun.waveController.totalWaveCredits *= spawnRate;
                infiniteTowerRun.waveController.creditsPerSecond *= spawnRate;
            }
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveSpirit(body);
            }
        }

        public override void Stop()
        {
            foreach (var master in spirited)
            {
                if (master == null || master.inventory == null)
                {
                    continue;
                }
                var inventory = master.inventory;
                if (inventory.GetItemCountPermanent(DLC4Content.Items.VengefulSpirit) > 0)
                {
                    inventory.RemoveItemPermanent(DLC4Content.Items.VengefulSpirit);
                }
                else if (inventory.GetItemCountPermanent(DLC4Content.Items.VengefulSpiritRageMode) > 0)
                {
                    inventory.RemoveItemPermanent(DLC4Content.Items.VengefulSpiritRageMode);
                }
            }
            spirited.Clear();
        }
    }
}
