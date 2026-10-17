using BepInEx.Configuration;
using HG;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventItemKits : AbstractEvent
    {
        private static readonly (string name, string items)[] DefaultKits =
        {
            ("Glass Cannon", "LunarDagger & 10xCritGlasses"),
            ("Bleed", "10xBleedOnHit & 3xCritGlasses & BleedOnHitAndExplode"),
            ("Bounce House", "5xFeather & 5xJumpBoost & FallBoots"),
            ("Proc Party", "3xChainLightning & 5xStickyBomb & 3xMissile & 3xIgniteOnKill"),
            ("Speedster", "5xHoof & 5xSprintBonus & 5xSyringe"),
            ("Tank", "5xBear & 10xArmorPlate & 10xFlatHealth & 2xKnurl"),
            ("Elemental", "2xFireRing & 2xIceRing & FireballsOnHit & LightningStrikeOnHit"),
        };
        private readonly List<(string name, ConfigEntry<string> items)> kits = new List<(string name, ConfigEntry<string> items)>();
        private string kitName;
        private string kitItems;

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
            return Language.GetStringFormatted("ANNOUNCE_EVENT_ITEM_KITS", kitName ?? DefaultKits[0].name);
        }
        public override string GetDescription()
        {
            return "Gives all players a random item kit until the event ends, e.g. Glass Cannon.";
        }
        public override string GetPopupDescription()
        {
            return "Gives all players the items of the kit temporarily.";
        }
        public override string GetConditionDescription()
        {
            return "At least one kit has items.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            foreach (var (name, items) in DefaultKits)
            {
                kits.Add((name, config.Bind(GetEventConfigName(), $"Kit {name}", items, $"Items of the {name} kit. Same format as StartBonusMod's item list, e.g. 5xHoof & Syringe. Leave empty to drop the kit.")));
            }
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
            // players who respawn during the event get the kit again
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                body.gameObject.EnsureComponent<ItemKitComponent>().Give(kitItems);
            }
        }

        private List<(string name, ConfigEntry<string> items)> GetKits()
        {
            return kits.Where(kit => !string.IsNullOrWhiteSpace(kit.items.Value)).ToList();
        }

        public override void Prepare()
        {
            var candidates = GetKits();
            if (candidates.Count == 0)
            {
                kitName = null;
                kitItems = null;
                return;
            }
            var kit = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
            kitName = kit.name;
            kitItems = kit.items.Value;
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            if (kitItems == null)
            {
                Prepare();
            }
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                body.gameObject.EnsureComponent<ItemKitComponent>().Give(kitItems);
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
            kitName = null;
            kitItems = null;
        }

        public class ItemKitComponent : MonoBehaviour
        {
            private TemporaryInventory inventory;

            public void Give(string itemString)
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
                        PurchaseInteraction.CreateItemTakenOrb(body.corePosition, body.gameObject, itemIndex);
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
