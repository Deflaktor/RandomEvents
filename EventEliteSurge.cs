using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventEliteSurge : AbstractEvent
    {
        private ConfigEntry<float> SpawnRate;
        private ConfigEntry<float> EliteChance;
        private ConfigEntry<float>[] GainedEliteWeights;
        private WeightedSelection<EquipmentDef> aspects;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetAspects().Count > 0;
        }
        public override string GetEventConfigName()
        {
            return "EliteSurge";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_ELITE_SURGE");
        }
        public override string GetDescription()
        {
            return "Every monster gains random elite types, usually one, sometimes two or three, with stronger ones rarer and without an elite's extra health and damage. A quarter fewer monsters spawn, and every player gets a Wake of Vultures.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            SpawnRate = config.Bind(GetEventConfigName(), "Spawn Rate", 0.75f, "How many monsters spawn during the event, relative to normal.");
            EliteChance = config.Bind(GetEventConfigName(), "Elite Chance", 1f, "Probability for a monster to gain elite types. A monster that spawns as an elite anyway gains them on top of its own.");
            GainedEliteWeights = new[]
            {
                config.Bind(GetEventConfigName(), "Weight 1 Gained Elite", 1f, "Weight for a monster to gain one elite type."),
                config.Bind(GetEventConfigName(), "Weight 2 Gained Elites", 0.25f, "Weight for a monster to gain two elite types."),
                config.Bind(GetEventConfigName(), "Weight 3 Gained Elites", 0.05f, "Weight for a monster to gain three elite types."),
            };
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.CombatDirector.DirectorMoneyWave.Update += DirectorMoneyWave_Update;
        }

        // The aspects of the elites the director can spawn, of Voidtouched and of Perfected, each weighted by how cheap
        // its elite is for the director, so stronger elites are rarer
        private static WeightedSelection<EquipmentDef> GetAspects()
        {
            // by aspect, since the Artifact of Honor's cheaper elites share theirs with the normal ones
            var costs = new Dictionary<EquipmentDef, float>();
            foreach (var tier in CombatDirector.eliteTiers)
            {
                if (tier == null || tier.eliteTypes == null || tier.costMultiplier <= 1f)
                {
                    continue;
                }
                foreach (var eliteDef in tier.eliteTypes)
                {
                    if (eliteDef != null && eliteDef.eliteEquipmentDef != null && eliteDef.IsAvailable())
                    {
                        costs[eliteDef.eliteEquipmentDef] = Mathf.Max(costs.GetValueOrDefault(eliteDef.eliteEquipmentDef), tier.costMultiplier);
                    }
                }
            }
            // as rare as the strongest elites, unlike on the moon, where Perfected elites are common
            foreach (var eliteDef in new[] { DLC1Content.Elites.Void, RoR2Content.Elites.Lunar })
            {
                if (eliteDef != null && eliteDef.eliteEquipmentDef != null && eliteDef.IsAvailable())
                {
                    costs[eliteDef.eliteEquipmentDef] = CombatDirector.baseEliteCostMultiplier * 6f;
                }
            }
            var selection = new WeightedSelection<EquipmentDef>();
            foreach (var (aspect, cost) in costs)
            {
                selection.AddChoice(aspect, 1f / cost);
            }
            return selection;
        }

        // An aspect gives an elite's powers, while its extra health and damage come from items the director adds
        // when it spawns an elite, so the gained elite types keep the monster's normal stats. A monster that isn't
        // an elite yet holds the first aspect; the others, like an elite's own, work through their buff, as with
        // Wake of Vultures.
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                GiveWakeOfVultures(body);
                return;
            }
            if (!NetworkServer.active || !IsActive() || aspects == null || aspects.Count == 0 || body.master == null || body.inventory == null
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral
                || RoR2Application.rng.nextNormalizedFloat >= EliteChance.Value)
            {
                return;
            }
            var counts = new WeightedSelection<int>();
            for (int i = 0; i < GainedEliteWeights.Length; i++)
            {
                counts.AddChoice(i + 1, Mathf.Max(0f, GainedEliteWeights[i].Value));
            }
            if (counts.totalWeight <= 0f)
            {
                return;
            }
            var held = new HashSet<EquipmentDef>();
            var heldAspect = EquipmentCatalog.GetEquipmentDef(body.inventory.currentEquipmentIndex);
            if (heldAspect != null && EliteCatalog.GetEliteDefFromEquipmentIndex(heldAspect.equipmentIndex) != null)
            {
                held.Add(heldAspect);
            }
            var count = counts.Evaluate(RoR2Application.rng.nextNormalizedFloat);
            for (int i = 0; i < count; i++)
            {
                var aspect = PickAspect(held);
                if (aspect == null)
                {
                    return;
                }
                if (held.Count == 0)
                {
                    body.inventory.SetEquipmentIndex(aspect.equipmentIndex, body.inventory.activeEquipmentLocation);
                }
                else if (aspect.passiveBuffDef != null)
                {
                    body.AddBuff(aspect.passiveBuffDef);
                }
                held.Add(aspect);
            }
        }

        // so players can take the elites' powers for themselves; also to players who respawn during the event
        private void GiveWakeOfVultures(CharacterBody body)
        {
            if (body.inventory == null)
            {
                return;
            }
            var inventory = EventLoot.GetInventory(this, body);
            if (inventory.temporaryItems.ContainsKey(RoR2Content.Items.HeadHunter.itemIndex))
            {
                return;
            }
            inventory.GiveTemporaryItem(RoR2Content.Items.HeadHunter.itemIndex);
            PurchaseInteraction.CreateItemTakenOrb(body.corePosition + Vector3.up * 4f, body.gameObject, RoR2Content.Items.HeadHunter.itemIndex);
        }

        // a random aspect the monster doesn't have yet
        private EquipmentDef PickAspect(HashSet<EquipmentDef> held)
        {
            var selection = new WeightedSelection<EquipmentDef>();
            for (int i = 0; i < aspects.Count; i++)
            {
                var choice = aspects.GetChoice(i);
                if (!held.Contains(choice.value))
                {
                    selection.AddChoice(choice);
                }
            }
            return selection.Count > 0 ? selection.Evaluate(RoR2Application.rng.nextNormalizedFloat) : null;
        }

        // a quarter fewer monsters, since all of them are elites
        private float DirectorMoneyWave_Update(On.RoR2.CombatDirector.DirectorMoneyWave.orig_Update orig, CombatDirector.DirectorMoneyWave self, float deltaTime, float difficultyCoefficient)
        {
            var credits = orig(self, deltaTime, difficultyCoefficient);
            return NetworkServer.active && IsActive() ? credits * Mathf.Max(0f, SpawnRate.Value) : credits;
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            aspects = GetAspects();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                GiveWakeOfVultures(body);
            }
            // Simulacrum's waves get their credits from the wave instead
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null)
            {
                var spawnRate = Mathf.Max(0f, SpawnRate.Value);
                infiniteTowerRun.waveController.totalWaveCredits *= spawnRate;
                infiniteTowerRun.waveController.creditsPerSecond *= spawnRate;
            }
        }

        public override void Stop()
        {
            aspects = null;
            EventLoot.Clear(this);
        }
    }
}
