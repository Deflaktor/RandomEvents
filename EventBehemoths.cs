using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventBehemoths : AbstractEvent
    {
        private ConfigEntry<int> PlayerBehemoths;
        private ConfigEntry<int> MonsterBehemoths;
        private ConfigEntry<float> MonsterDamageMultiplier;
        private ConfigEntry<int> VoidsentFlames;
        private ConfigEntry<float> FlameDamageTakenMultiplier;
        private ConfigEntry<int> WillOTheWisps;
        private ConfigEntry<float> WispDamageTakenMultiplier;
        private readonly HashSet<CharacterBody> armed = new HashSet<CharacterBody>();

        public override EventTags Tags => EventTags.EventItems;
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
            return "Behemoths";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_BEHEMOTHS");
        }
        public override string GetDescription()
        {
            return "All players and monsters get 3 Brilliant Behemoths, a Will-o'-the-wisp and a Voidsent Flame, but monsters deal half damage, and players take half the damage from the wisps and flames.";
        }
        public override string GetPopupDescription()
        {
            return "Everyone gets 3 Brilliant Behemoths, a Will-o'-the-wisp and a Voidsent Flame. Monsters deal half damage.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            PlayerBehemoths = config.Bind(GetEventConfigName(), "Player Behemoths", 3, "How many Brilliant Behemoths each player gets.");
            MonsterBehemoths = config.Bind(GetEventConfigName(), "Monster Behemoths", 3, "How many Brilliant Behemoths each monster gets.");
            MonsterDamageMultiplier = config.Bind(GetEventConfigName(), "Monster Damage Multiplier", 0.5f, "The damage monsters deal during the event, relative to normal.");
            VoidsentFlames = config.Bind(GetEventConfigName(), "Voidsent Flames", 1, "How many Voidsent Flames each player and monster gets.");
            WillOTheWisps = config.Bind(GetEventConfigName(), "Wisps", 1, "How many Will-o'-the-wisps each player and monster gets.");
            WispDamageTakenMultiplier = config.Bind(GetEventConfigName(), "Wisp Damage Taken Multiplier", 0.5f, "The damage players take from the monsters' Will-o'-the-wisps, relative to normal, on top of the Monster Damage Multiplier.");
            FlameDamageTakenMultiplier = config.Bind(GetEventConfigName(), "Voidsent Flame Damage Taken Multiplier", 0.5f, "The damage players take from the monsters' Voidsent Flames, relative to normal, on top of the Monster Damage Multiplier.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.DelayBlast.Detonate += DelayBlast_Detonate;
            On.RoR2.Inventory.ItemTransformation.TryTransform += ItemTransformation_TryTransform;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // also those who spawn or respawn during the event
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveBehemoths(body);
            }
        }

        private void GiveBehemoths(CharacterBody body)
        {
            if (body == null || body.inventory == null || armed.Contains(body) || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            var count = body.isPlayerControlled ? PlayerBehemoths.Value : !Helper.IsPlayersSide(body) ? MonsterBehemoths.Value : 0;
            if (count <= 0)
            {
                return;
            }
            armed.Add(body);
            var inventory = EventLoot.GetInventory(this, body);
            inventory.GiveTemporaryItem(RoR2Content.Items.Behemoth.itemIndex, count);
            if (VoidsentFlames.Value > 0)
            {
                inventory.GiveTemporaryItem(DLC1Content.Items.ExplodeOnDeathVoid.itemIndex, VoidsentFlames.Value);
            }
            if (WillOTheWisps.Value > 0)
            {
                inventory.GiveTemporaryItem(RoR2Content.Items.ExplodeOnDeath.itemIndex, WillOTheWisps.Value);
            }
            if (body.isPlayerControlled)
            {
                ItemOrb.Fly(body.corePosition, body.gameObject, RoR2Content.Items.Behemoth.itemIndex);
            }
        }

        // all of the monsters' damage, also their blasts
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && damageInfo != null && damageInfo.attacker != null && damageInfo.attacker.TryGetComponent(out CharacterBody attackerBody)
                && !Helper.IsPlayersSide(attackerBody) && attackerBody.teamComponent.teamIndex != TeamIndex.Neutral)
            {
                damageInfo.damage *= Mathf.Max(0f, MonsterDamageMultiplier.Value);
            }
            orig(self, damageInfo);
        }

        // A monster's Voidsent Flame or Will-o'-the-wisp blast hits only players and their allies, so a smaller blast
        // hurts them less.
        private void DelayBlast_Detonate(On.RoR2.DelayBlast.orig_Detonate orig, DelayBlast self)
        {
            if (NetworkServer.active && IsActive() && self.attacker != null && self.attacker.TryGetComponent(out CharacterBody attackerBody)
                && !Helper.IsPlayersSide(attackerBody))
            {
                var flame = HealthComponent.AssetReferences.explodeOnDeathVoidExplosionPrefab;
                var wisp = GlobalEventManager.CommonAssets.explodeOnDeathPrefab;
                if (flame != null && self.name.StartsWith(flame.name))
                {
                    self.baseDamage *= Mathf.Max(0f, FlameDamageTakenMultiplier.Value);
                }
                else if (wisp != null && self.name.StartsWith(wisp.name))
                {
                    self.baseDamage *= Mathf.Max(0f, WispDamageTakenMultiplier.Value);
                }
            }
            orig(self);
        }

        // The event's Voidsent Flames would turn the players' own Will-o'-the-wisps into more of them for good.
        private bool ItemTransformation_TryTransform(On.RoR2.Inventory.ItemTransformation.orig_TryTransform orig, ref Inventory.ItemTransformation self, Inventory inventory, out Inventory.ItemTransformation.TryTransformResult result)
        {
            if (NetworkServer.active && IsActive() && self.newItemIndex == DLC1Content.Items.ExplodeOnDeathVoid.itemIndex)
            {
                result = default;
                return false;
            }
            return orig(ref self, inventory, out result);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            armed.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveBehemoths(body);
            }
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
            armed.Clear();
        }
    }
}
