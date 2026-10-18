using BepInEx.Configuration;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventBouncy : AbstractEvent
    {
        private ConfigEntry<int> VesselCount;
        private ConfigEntry<int> MonsterFeathers;
        private ConfigEntry<float> MinHopInterval;
        private ConfigEntry<float> MaxHopInterval;
        // when each monster jumps next
        private readonly Dictionary<CharacterBody, float> nextHops = new Dictionary<CharacterBody, float>();
        // monsters that walk, and so can jump

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Earthbound Vessel comes with that expansion; DroneUprising's drones mostly fly
            return !Helper.IsSimulacrumBossWaveNext() && DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4)
                && MonsterPool.GetChoices().Any(IsWalker)
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("DroneUprising", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "Bouncy";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_BOUNCY");
        }
        public override string GetDescription()
        {
            return "All players get 3 Earthbound Vessels. Only walking monsters spawn, and they jump at random, also in mid-air.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, not a Simulacrum boss wave, the stage has walking monsters, and event \"DroneUprising\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            VesselCount = config.Bind(GetEventConfigName(), "Vessel Count", 3, "How many Earthbound Vessels each player gets.");
            MonsterFeathers = config.Bind(GetEventConfigName(), "Monster Feathers", 1, "How many Hopoo Feathers each monster gets, each an extra jump in mid-air.");
            MinHopInterval = config.Bind(GetEventConfigName(), "Min Jump Interval", 1f, "The shortest time between two jumps of a monster, in seconds.");
            MaxHopInterval = config.Bind(GetEventConfigName(), "Max Jump Interval", 4f, "The longest time between two jumps of a monster, in seconds.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.CharacterAI.BaseAI.UpdateBodyInputs += BaseAI_UpdateBodyInputs;
        }

        // ground monsters that can move
        private static bool IsWalker(WeightedSelection<DirectorCard>.ChoiceInfo choice)
        {
            var spawnCard = choice.value != null ? choice.value.spawnCard : null;
            var master = spawnCard != null && spawnCard.prefab != null ? spawnCard.prefab.GetComponent<CharacterMaster>() : null;
            var body = master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
            return choice.weight > 0f && body != null && body.baseMoveSpeed > 0f && spawnCard.nodeGraphType == MapNodeGroup.GraphType.Ground;
        }

        // players who respawn during the event get theirs again
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveVessels(body);
                GiveFeathers(body);
            }
        }

        private void GiveVessels(CharacterBody body)
        {
            if (body == null || !body.isPlayerControlled || body.inventory == null || VesselCount.Value <= 0)
            {
                return;
            }
            var inventory = EventLoot.GetInventory(this, body);
            if (inventory.temporaryItems.ContainsKey(DLC4Content.Items.RechargeableJump.itemIndex))
            {
                return;
            }
            inventory.GiveTemporaryItem(DLC4Content.Items.RechargeableJump.itemIndex, VesselCount.Value);
            PurchaseInteraction.CreateItemTakenOrb(body.corePosition + Vector3.up * 4f, body.gameObject, DLC4Content.Items.RechargeableJump.itemIndex);
        }

        // a Feather for a jump in mid-air, as the monsters only jump the normal way
        private void GiveFeathers(CharacterBody body)
        {
            if (body == null || body.isPlayerControlled || body.inventory == null || body.characterMotor == null || MonsterFeathers.Value <= 0
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            var inventory = EventLoot.GetInventory(this, body);
            if (!inventory.temporaryItems.ContainsKey(RoR2Content.Items.Feather.itemIndex))
            {
                inventory.GiveTemporaryItem(RoR2Content.Items.Feather.itemIndex, MonsterFeathers.Value);
            }
        }

        // The monsters' AI runs on the host, so pressing jump for them makes them jump like they do on their own,
        // with their jump height, animation and sound; it does nothing for monsters that can't jump.
        private void BaseAI_UpdateBodyInputs(On.RoR2.CharacterAI.BaseAI.orig_UpdateBodyInputs orig, BaseAI self)
        {
            orig(self);
            var body = self.body;
            if (!NetworkServer.active || !IsActive() || Run.instance == null || body == null || self.bodyInputBank == null || body.characterMotor == null
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            var now = Run.instance.fixedTime;
            if (!nextHops.TryGetValue(body, out var nextHop))
            {
                nextHops[body] = now + NextInterval();
                return;
            }
            if (now < nextHop)
            {
                return;
            }
            nextHops[body] = now + NextInterval();
            // a fresh press, which the body's state reads as a jump
            self.bodyInputBank.jump.down = true;
        }

        private float NextInterval()
        {
            var min = Mathf.Max(0.1f, MinHopInterval.Value);
            return RoR2Application.rng.RangeFloat(min, Mathf.Max(min, MaxHopInterval.Value));
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            nextHops.Clear();
            // with the pool of other events that filter it; unless no monster would be left
            MonsterPool.Filter(this, IsWalker);
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveVessels(body);
                GiveFeathers(body);
            }
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
            EventLoot.Clear(this);
            nextHops.Clear();
        }
    }
}
