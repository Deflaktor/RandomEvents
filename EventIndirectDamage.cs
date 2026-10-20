using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventIndirectDamage : AbstractEvent
    {
        // items whose effects on hit deal damage; only the run's common and uncommon ones are used
        private static ItemDef[] OnHitItems => new[]
        {
            RoR2Content.Items.BleedOnHit, RoR2Content.Items.StickyBomb, RoR2Content.Items.ChainLightning, RoR2Content.Items.Missile,
            RoR2Content.Items.FireRing, RoR2Content.Items.IceRing,
            DLC2Content.Items.StunAndPierce, DLC3Content.Items.SharedSuffering, DLC4Content.Items.WispOnKill,
            DLC4Content.Items.DreamDemon, DLC4Content.Items.CausticPheromones,
        };
        private static readonly DamageColorIndex[] IndirectDamageColors = { DamageColorIndex.Item, DamageColorIndex.Bleed, DamageColorIndex.SuperBleed, DamageColorIndex.Poison, DamageColorIndex.Electrocution };
        private ConfigEntry<int> CommonItems;
        private ConfigEntry<int> UncommonItems;
        // each player's items, which they get again when they respawn
        private readonly Dictionary<CharacterMaster, List<ItemIndex>> rolledItems = new Dictionary<CharacterMaster, List<ItemIndex>>();
        // only this event's own, other events give temporary items too
        private readonly Dictionary<CharacterBody, TemporaryInventory> inventories = new Dictionary<CharacterBody, TemporaryInventory>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetCandidates(ItemTier.Tier1).Count > 0 || GetCandidates(ItemTier.Tier2).Count > 0
                // Cursed Spoils' hexes would disable the event's items
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("CursedSpoils", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "IndirectDamage";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_INDIRECT_DAMAGE");
        }
        public override string GetDescription()
        {
            return "Players' hits deal no damage, but their item effects and damage over time still do. All players get 10 common and 5 uncommon items with on-hit effects.";
        }
        public override string GetPopupDescription()
        {
            return "Your hits deal no damage, but your item effects and damage over time still do. You get 10 common and 5 uncommon items with on-hit effects temporarily.";
        }
        public override string GetConditionDescription()
        {
            return "At least one common or uncommon item with an on-hit effect is available, and event \"CursedSpoils\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            CommonItems = config.Bind(GetEventConfigName(), "Common Items", 10, "How many random common items with on-hit effects each player gets.");
            UncommonItems = config.Bind(GetEventConfigName(), "Uncommon Items", 5, "How many random uncommon items with on-hit effects each player gets.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        // A direct hit deals no damage, but keeps its damage for the items it sets off afterwards, which scale with it
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && damageInfo != null && damageInfo.attacker != null && self.body != null
                && damageInfo.attacker.TryGetComponent(out CharacterBody attackerBody) && attackerBody.isPlayerControlled
                && attackerBody.teamComponent.teamIndex != self.body.teamComponent.teamIndex && IsDirect(damageInfo))
            {
                var damage = damageInfo.damage;
                damageInfo.damage = 0f;
                orig(self, damageInfo);
                damageInfo.damage = damage;
                return;
            }
            orig(self, damageInfo);
        }

        // items' hits carry the proc that set them off or the item damage color, damage over time its own
        private static bool IsDirect(DamageInfo damageInfo)
        {
            return damageInfo.procChainMask.mask == 0 && damageInfo.dotIndex == DotController.DotIndex.None
                && (damageInfo.damageType.damageType & DamageType.DoT) == 0 && !IndirectDamageColors.Contains(damageInfo.damageColorIndex);
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled && body.master != null)
            {
                GiveItems(body);
            }
        }

        private static List<ItemIndex> GetCandidates(ItemTier tier)
        {
            return OnHitItems.Where(itemDef => itemDef != null && itemDef.tier == tier
                    && (Run.instance == null || Run.instance.availableItems.Contains(itemDef.itemIndex)))
                .Select(itemDef => itemDef.itemIndex).ToList();
        }

        private static IEnumerable<ItemIndex> Roll(ItemTier tier, int count)
        {
            var candidates = GetCandidates(tier);
            for (int i = 0; candidates.Count > 0 && i < count; i++)
            {
                yield return candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
            }
        }

        private void GiveItems(CharacterBody body)
        {
            if (body.inventory == null || inventories.ContainsKey(body))
            {
                return;
            }
            if (!rolledItems.TryGetValue(body.master, out var items))
            {
                items = Roll(ItemTier.Tier1, CommonItems.Value).Concat(Roll(ItemTier.Tier2, UncommonItems.Value)).ToList();
                rolledItems[body.master] = items;
            }
            var inventory = body.gameObject.AddComponent<TemporaryInventory>();
            inventories[body] = inventory;
            foreach (var itemGroup in items.GroupBy(itemIndex => itemIndex))
            {
                inventory.GiveTemporaryItem(itemGroup.Key, itemGroup.Count());
                ItemOrb.Fly(body.corePosition + Vector3.up * 5f, body.gameObject, itemGroup.Key);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            rolledItems.Clear();
            inventories.Clear();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                GiveItems(body);
            }
        }

        public override void Stop()
        {
            foreach (var inventory in inventories.Values)
            {
                if (inventory != null)
                {
                    UnityEngine.Object.Destroy(inventory);
                }
            }
            inventories.Clear();
            rolledItems.Clear();
        }
    }
}
