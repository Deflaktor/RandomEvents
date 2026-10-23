using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventNeutroniumWeights : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        // the monsters given a weight, so they don't get another one after it was sent away
        private readonly HashSet<CharacterMaster> weighted = new HashSet<CharacterMaster>();
        // the weights the event gave or moved as temporary stacks, taken back when the event ends
        private readonly Dictionary<Inventory, int> changes = new Dictionary<Inventory, int>();
        // the orbs carrying weights sent during the event
        private readonly HashSet<Orb> orbs = new HashSet<Orb>();
        // the projectiles carrying weights sent during the event; only the weight sends this kind
        private readonly HashSet<ProjectileTransferItem> projectiles = new HashSet<ProjectileTransferItem>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Neutronium Weight comes with that expansion
            return DLC3Content.DLC3 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC3Content.DLC3);
        }
        public override string GetEventConfigName()
        {
            return "NeutroniumWeights";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_NEUTRONIUM_WEIGHTS");
        }
        public override string GetDescription()
        {
            return "All monsters get a Neutronium Weight until the event ends.";
        }
        public override string GetConditionDescription()
        {
            return "Alloyed Collective is enabled.";
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
            On.RoR2.Inventory.GiveItemPermanent_ItemIndex_int += Inventory_GiveItemPermanent;
            On.RoR2.Orbs.OrbManager.AddOrb += OrbManager_AddOrb;
            On.RoR2.Orbs.ItemTransferOrbDroppable.OnArrival += ItemTransferOrbDroppable_OnArrival;
            On.RoR2.Projectile.ProjectileTransferItem.Start += ProjectileTransferItem_Start;
            On.RoR2.Projectile.ProjectileTransferItem.DropItem += ProjectileTransferItem_DropItem;
        }

        private void ProjectileTransferItem_Start(On.RoR2.Projectile.ProjectileTransferItem.orig_Start orig, ProjectileTransferItem self)
        {
            if (NetworkServer.active && IsActive())
            {
                projectiles.Add(self);
            }
            orig(self);
        }

        // a projectile of the event that hits no one doesn't drop its weights as pickups
        private void ProjectileTransferItem_DropItem(On.RoR2.Projectile.ProjectileTransferItem.orig_DropItem orig, ProjectileTransferItem self)
        {
            if (!projectiles.Remove(self))
            {
                orig(self);
            }
        }

        // the game moves weights as real items, when sent on hit or to the killer; during the event they move as
        // temporary stacks instead
        private void Inventory_GiveItemPermanent(On.RoR2.Inventory.orig_GiveItemPermanent_ItemIndex_int orig, Inventory self, ItemIndex itemIndex, int countToAdd)
        {
            if (NetworkServer.active && IsActive() && itemIndex == DLC3Content.Items.TransferDebuffOnHit.itemIndex)
            {
                Change(self, countToAdd);
                return;
            }
            orig(self, itemIndex, countToAdd);
        }

        private void OrbManager_AddOrb(On.RoR2.Orbs.OrbManager.orig_AddOrb orig, OrbManager self, Orb orb)
        {
            if (NetworkServer.active && IsActive() && orb is ItemTransferOrbDroppable itemOrb && itemOrb.itemIndex == DLC3Content.Items.TransferDebuffOnHit.itemIndex)
            {
                orbs.Add(orb);
            }
            orig(self, orb);
        }

        // An orb of the event never drops its weight as a pickup when its target died on the way, and gives nothing once
        // the event ended.
        private void ItemTransferOrbDroppable_OnArrival(On.RoR2.Orbs.ItemTransferOrbDroppable.orig_OnArrival orig, ItemTransferOrbDroppable self)
        {
            if (!orbs.Remove(self))
            {
                orig(self);
                return;
            }
            if (IsActive() && self.target != null && self.target.healthComponent != null && self.target.healthComponent.alive && self.inventoryToGrantTo != null)
            {
                Change(self.inventoryToGrantTo, self.stack);
            }
        }

        private void Change(Inventory inventory, int count)
        {
            inventory.GiveItemChanneled(DLC3Content.Items.TransferDebuffOnHit.itemIndex, count);
            changes[inventory] = changes.GetValueOrDefault(inventory) + count;
        }

        // monsters spawning during the event get one too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveWeight(body);
            }
        }

        private void GiveWeight(CharacterBody body)
        {
            var master = body != null ? body.master : null;
            if (master == null || master.inventory == null || !weighted.Add(master)
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            Change(master.inventory, 1);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            weighted.Clear();
            changes.Clear();
            orbs.Clear();
            projectiles.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveWeight(body);
            }
        }

        public override void Stop()
        {
            foreach (var (inventory, count) in changes)
            {
                if (inventory != null && count != 0)
                {
                    inventory.GiveItemChanneled(DLC3Content.Items.TransferDebuffOnHit.itemIndex, -count);
                }
            }
            changes.Clear();
            weighted.Clear();
            // the ones still flying would give real weights
            foreach (var projectile in projectiles)
            {
                if (projectile != null)
                {
                    NetworkServer.Destroy(projectile.gameObject);
                }
            }
            projectiles.Clear();
        }
    }
}
