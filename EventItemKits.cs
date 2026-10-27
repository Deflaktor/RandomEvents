using BepInEx.Configuration;
using HG;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventItemKits : AbstractEvent
    {
        // the monsters get the kit too, and deal this much of their damage
        private static readonly (string name, string items, float monsterDamage)[] DefaultKits =
        {
            ("Glass Cannon", "LunarDagger & 10xCritGlasses", 0.25f),
            ("Bleed", "10xBleedOnHit & 3xCritGlasses & BleedOnHitAndExplode", 0.5f),
            ("Bounce House", "5xFeather & 5xJumpBoost & FallBoots", 1f),
            ("Proc Party", "3xChainLightning & 5xStickyBomb & 3xMissile & 3xIgniteOnKill", 0.5f),
            ("Speedster", "5xHoof & 5xSprintBonus & 5xSyringe", 0.7f),
            ("Tank", "5xBear & 10xArmorPlate & 10xFlatHealth & 2xKnurl", 1f),
            ("Elemental", "2xFireRing & 2xIceRing & FireballsOnHit & LightningStrikeOnHit", 0.5f),
        };
        private readonly List<Kit> kits = new List<Kit>();
        private Kit kit;
        private readonly HashSet<ItemIndex> kitItemIndices = new HashSet<ItemIndex>();

        private class Kit
        {
            public string name;
            public ConfigEntry<string> items;
            public ConfigEntry<float> monsterDamage;
        }

        public override EventTags Tags => EventTags.EventItems;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetKits().Count > 0;
        }
        public override string GetEventConfigName()
        {
            return "ItemKits";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_ITEM_KITS", kit?.name ?? DefaultKits[0].name);
        }
        public override string GetDescription()
        {
            return "Gives all players and monsters a random item kit until the event ends, e.g. Glass Cannon. Monsters deal less damage, depending on the kit.";
        }
        public override string GetPopupDescription()
        {
            return "You and the monsters get the items of this kit until the event ends.";
        }
        public override string GetConditionDescription()
        {
            return "At least one kit has items.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            foreach (var (name, items, monsterDamage) in DefaultKits)
            {
                kits.Add(new Kit
                {
                    name = name,
                    items = config.Bind(GetEventConfigName(), $"Kit {name}", items, $"Items of the {name} kit. Same format as StartBonusMod's item list, e.g. 5xHoof & Syringe. Leave empty to drop the kit."),
                    monsterDamage = config.Bind(GetEventConfigName(), $"Kit {name} Monster Damage", monsterDamage, $"The damage monsters deal during the {name} kit, relative to normal."),
                });
            }
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.Inventory.ItemTransformation.TryTransform += ItemTransformation_TryTransform;
        }

        // also players who respawn and monsters that spawn during the event
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive())
            {
                GiveKit(body);
            }
        }

        private void GiveKit(CharacterBody body)
        {
            if (kit == null || body == null || body.teamComponent == null || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            if (body.isPlayerControlled || !Helper.IsPlayersSide(body))
            {
                body.gameObject.EnsureComponent<ItemKitComponent>().Give(kit.items.Value, body.isPlayerControlled);
            }
        }

        // all of the monsters' damage, also their blasts
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && kit != null && damageInfo != null && damageInfo.attacker != null
                && damageInfo.attacker.TryGetComponent(out CharacterBody attackerBody)
                && !Helper.IsPlayersSide(attackerBody) && attackerBody.teamComponent.teamIndex != TeamIndex.Neutral)
            {
                damageInfo.damage *= Mathf.Max(0f, kit.monsterDamage.Value);
            }
            orig(self, damageInfo);
        }

        // The kit's void items would turn the players' own items into more of them for good.
        private bool ItemTransformation_TryTransform(On.RoR2.Inventory.ItemTransformation.orig_TryTransform orig, ref Inventory.ItemTransformation self, Inventory inventory, out Inventory.ItemTransformation.TryTransformResult result)
        {
            if (NetworkServer.active && IsActive() && kitItemIndices.Contains(self.newItemIndex))
            {
                result = default;
                return false;
            }
            return orig(ref self, inventory, out result);
        }

        private List<Kit> GetKits()
        {
            return kits.Where(kit => !string.IsNullOrWhiteSpace(kit.items.Value)).ToList();
        }

        public override void Prepare()
        {
            var candidates = GetKits();
            kit = candidates.Count > 0 ? candidates[RoR2Application.rng.RangeInt(0, candidates.Count)] : null;
            kitItemIndices.Clear();
            if (kit != null)
            {
                var resolvedItems = new Dictionary<PickupIndex, int>();
                ItemStringParser.ItemStringParser.ParseItemString(kit.items.Value, resolvedItems, Log._logSource, false);
                foreach (var pickupIndex in resolvedItems.Keys)
                {
                    var itemIndex = PickupCatalog.GetPickupDef(pickupIndex)?.itemIndex ?? ItemIndex.None;
                    if (itemIndex != ItemIndex.None)
                        kitItemIndices.Add(itemIndex);
                }
            }
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            if (kit == null)
            {
                Prepare();
            }
            foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
            {
                if (body.healthComponent != null && body.healthComponent.alive)
                    GiveKit(body);
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.TryGetComponent(out ItemKitComponent itemKit))
                {
                    GameObject.Destroy(itemKit);
                }
            }
            kit = null;
            kitItemIndices.Clear();
        }

        public class ItemKitComponent : MonoBehaviour
        {
            private TemporaryInventory inventory;

            public void Give(string itemString, bool showOrbs)
            {
                var body = GetComponent<CharacterBody>();
                if (inventory != null || string.IsNullOrWhiteSpace(itemString) || body == null || body.inventory == null)
                {
                    return;
                }
                inventory = gameObject.AddComponent<TemporaryInventory>();
                var resolvedItems = new Dictionary<PickupIndex, int>();
                ItemStringParser.ItemStringParser.ParseItemString(itemString, resolvedItems, Log._logSource, false);
                foreach (var (pickupIndex, amount) in resolvedItems)
                {
                    var itemIndex = PickupCatalog.GetPickupDef(pickupIndex)?.itemIndex ?? ItemIndex.None;
                    if (itemIndex != ItemIndex.None && amount > 0)
                    {
                        inventory.GiveTemporaryItem(itemIndex, amount);
                        if (showOrbs)
                            ItemOrb.Fly(body.corePosition, body.gameObject, itemIndex);
                    }
                }
            }

            private void OnDisable()
            {
                if (inventory != null)
                {
                    Destroy(inventory);
                }
            }
        }
    }
}
