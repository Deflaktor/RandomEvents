using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventSecondWind : AbstractEvent
    {
        private const float UpdateInterval = 0.5f;
        private ConfigEntry<float> StartMaxHealth;
        private ConfigEntry<float> EndMaxHealth;
        private ConfigEntry<float> SimulacrumGrowDuration;
        // the curse each player got from the event
        private readonly Dictionary<CharacterBody, int> curses = new Dictionary<CharacterBody, int>();
        private float startTime;
        private float updateTimer;

        public override EventTags Tags => EventTags.PlayerHealth;
        // a Fuel Array would explode as soon as the maximum health outgrows the health
        public override EventTags ExcludedTags => EventTags.PlayerHealth;
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
            return "SecondWind";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SECOND_WIND");
        }
        public override string GetDescription()
        {
            return "Everyone drops to 1% of their maximum health, which then grows to 1000% by the end of the event, in Simulacrum within 60 seconds, by the same factor every second.";
        }
        public override string GetPopupDescription()
        {
            return "Everyone drops to almost no health, but your maximum health keeps growing.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            StartMaxHealth = config.Bind(GetEventConfigName(), "Start Max Health", 0.01f, "The players' maximum health at the start of the event, relative to normal.");
            EndMaxHealth = config.Bind(GetEventConfigName(), "End Max Health", 10f, "The players' maximum health at the end of the event, relative to normal.");
            SimulacrumGrowDuration = config.Bind(GetEventConfigName(), "Simulacrum Grow Duration", 60f, "In Simulacrum, how many seconds the maximum health takes to grow to its end value. Elsewhere it grows over the whole event.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
            On.RoR2.Inventory.ItemTransformation.TryTransform += ItemTransformation_TryTransform;
        }

        // From the start to the end, the maximum health grows by the same factor each second, so it takes as long to go
        // from 1% to 10% as from 100% to 1000%.
        private float MaxHealthFactor()
        {
            var start = Mathf.Max(0.001f, StartMaxHealth.Value);
            var end = Mathf.Max(0.001f, EndMaxHealth.Value);
            // Simulacrum's events last until the wave ends
            var duration = Run.instance is InfiniteTowerRun ? SimulacrumGrowDuration.Value : ModConfig.EventDuration.Value;
            var progress = Mathf.Clamp01((Time.fixedTime - startTime) / Mathf.Max(1f, duration));
            return start * Mathf.Pow(end / start, progress);
        }

        // the Delicate Watch doesn't break while the health is so low that any hit takes the player to low health
        private bool ItemTransformation_TryTransform(On.RoR2.Inventory.ItemTransformation.orig_TryTransform orig, ref Inventory.ItemTransformation self, Inventory inventory, out Inventory.ItemTransformation.TryTransformResult result)
        {
            if (NetworkServer.active && IsActive() && self.originalItemIndex == DLC1Content.Items.FragileDamageBonus.itemIndex
                && inventory != null && inventory.TryGetComponent(out CharacterMaster master) && master.playerCharacterMasterController != null
                && MaxHealthFactor() < 1f)
            {
                result = default;
                return false;
            }
            return orig(ref self, inventory, out result);
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            updateTimer -= Time.fixedDeltaTime;
            if (updateTimer > 0f)
            {
                return;
            }
            updateTimer = UpdateInterval;
            var factor = MaxHealthFactor();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                SetMaxHealth(body, factor);
            }
        }

        // Curse lowers the maximum health and Boost Hp raises it, both of which the health bar shows. The health itself
        // stays, so a player has to heal up to the new maximum.
        private void SetMaxHealth(CharacterBody body, float factor)
        {
            curses.TryGetValue(body, out var curse);
            var otherCurse = body.GetBuffCount(RoR2Content.Buffs.PermanentCurse) - curse;
            // each stack of curse adds 1% to what the maximum health is divided by
            var newCurse = factor < 1f ? Mathf.RoundToInt((1f + otherCurse * 0.01f) * (1f / factor - 1f) / 0.01f) : 0;
            if (newCurse != curse)
            {
                body.SetBuffCount(RoR2Content.Buffs.PermanentCurse.buffIndex, Math.Max(0, otherCurse + newCurse));
                curses[body] = newCurse;
            }
            // each Boost Hp adds 10% of the base health
            var inventory = EventLoot.GetInventory(this, body);
            var boostHp = inventory.temporaryItems.TryGetValue(RoR2Content.Items.BoostHp.itemIndex, out var count) ? count : 0;
            var newBoostHp = factor > 1f ? Mathf.RoundToInt((factor - 1f) * 10f) : 0;
            if (newBoostHp > boostHp)
            {
                inventory.GiveTemporaryItem(RoR2Content.Items.BoostHp.itemIndex, newBoostHp - boostHp);
            }
            else if (newBoostHp < boostHp)
            {
                inventory.RemoveTemporaryItem(RoR2Content.Items.BoostHp.itemIndex, boostHp - newBoostHp);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            curses.Clear();
            startTime = Time.fixedTime;
            updateTimer = UpdateInterval;
            var factor = MaxHealthFactor();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                SetMaxHealth(body, factor);
            }
        }

        public override void Stop()
        {
            foreach (var (body, stacks) in curses)
            {
                if (body != null)
                {
                    body.SetBuffCount(RoR2Content.Buffs.PermanentCurse.buffIndex, Math.Max(0, body.GetBuffCount(RoR2Content.Buffs.PermanentCurse) - stacks));
                }
            }
            curses.Clear();
            EventLoot.Clear(this);
        }
    }
}
