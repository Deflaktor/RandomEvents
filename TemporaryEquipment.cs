using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements.UIR;
using static RandomEvents.TemporaryInventory;
using static Rewired.InputMapper;
using static Rewired.Utils.Classes.Utility.ObjectInstanceTracker;

namespace RandomEvents
{
    public class TemporaryEquipment : MonoBehaviour
    {
        // The equipment belongs to the master's inventory, which all bodies of a player share. Two bodies can have a
        // TemporaryEquipment at once (e.g. a respawn while the old body is still there), so they share one snapshot of
        // the player's own equipment: the first one takes it, the last one restores it.
        private class Snapshot
        {
            public Inventory.EquipmentStateSlot[] real;
            public int users;
        }
        private static readonly Dictionary<Inventory, Snapshot> snapshots = new Dictionary<Inventory, Snapshot>();

        private Inventory snapshotInventory;
        private Inventory.EquipmentStateSlot[] realEquipmentStateSlots;
        private Inventory.EquipmentStateSlot[] temporaryEquipmentStateSlots;
        public float cooldownScale = 1f;
        public bool doNotDisableEquipment = false;

        // the inventories of an earlier run are gone
        public static void ClearSnapshots()
        {
            snapshots.Clear();
        }

        public static void Hook()
        {
            //On.RoR2.Inventory.SetEquipmentInternal += Inventory_SetEquipmentInternal;
            On.RoR2.CostTypeDef.IsAffordable += (orig, self, cost, activator) => { using (new TemporarilyRemoveEquipment(activator)) { return orig(self, cost, activator); } };
            On.RoR2.EquipmentDef.AttemptGrant += EquipmentDef_AttemptGrant;
            On.RoR2.Inventory.CalculateEquipmentCooldownScale += Inventory_CalculateEquipmentCooldownScale;
            On.RoR2.Inventory.SetEquipmentDisabled += Inventory_SetEquipmentDisabled;
        }

        private static void EquipmentDef_AttemptGrant(On.RoR2.EquipmentDef.orig_AttemptGrant orig, ref PickupDef.GrantContext context)
        {
            using (new TemporarilyRestoreOriginalEquipment(ref context)) { orig(ref context); }
        }

        // The TemporaryEquipment of the inventory's current body, if any. The game asks every fixed update for every
        // inventory, also ones without a master (e.g. Simulacrum's enemy items) or without a body, so this must stay quiet
        private static TemporaryEquipment FindTemporaryEquipment(Inventory inventory)
        {
            if (inventory.TryGetComponent(out CharacterMaster characterMaster) && characterMaster.bodyInstanceObject
                && characterMaster.bodyInstanceObject.TryGetComponent(out TemporaryEquipment temporaryEquipment))
            {
                return temporaryEquipment;
            }
            return null;
        }

        private static void Inventory_SetEquipmentDisabled(On.RoR2.Inventory.orig_SetEquipmentDisabled orig, Inventory self, bool _active)
        {
            var temporaryEquipment = FindTemporaryEquipment(self);
            if (temporaryEquipment && temporaryEquipment.doNotDisableEquipment)
            {
                return;
            }
            orig(self, _active);
        }

        private static float Inventory_CalculateEquipmentCooldownScale(On.RoR2.Inventory.orig_CalculateEquipmentCooldownScale orig, Inventory self)
        {
            var temporaryEquipment = FindTemporaryEquipment(self);
            if (temporaryEquipment)
            {
                return temporaryEquipment.cooldownScale * orig(self);
            }
            return orig(self);
        }

        public class TemporarilyRemoveEquipment : IDisposable
        {
            Inventory inventory;
            // a copy of the equipment as it was: after a pickup the inventory uses temporaryEquipmentStateSlots itself,
            // so clearing the inventory would clear that too
            Inventory.EquipmentStateSlot[] savedEquipmentStateSlots;

            public TemporarilyRemoveEquipment(Interactor activator)
            {
                var body = activator ? activator.GetComponent<CharacterBody>() : null;
                inventory = body ? body.inventory : null;
                if (inventory && activator.GetComponent<TemporaryEquipment>() != null)
                {
                    savedEquipmentStateSlots = Helper.CopyEquipmentState(inventory._equipmentSlots);
                    Helper.FillEquipment(inventory, EquipmentIndex.None);
                }
            }

            public void Dispose()
            {
                if (savedEquipmentStateSlots != null)
                {
                    Helper.TransferEquipmentIndex(savedEquipmentStateSlots, inventory._equipmentSlots);
                }
            }
        }

        public class TemporarilyRestoreOriginalEquipment : IDisposable
        {
            Inventory inventory;
            TemporaryEquipment temporaryEquipment;

            public TemporarilyRestoreOriginalEquipment(ref PickupDef.GrantContext context)
            {
                inventory = context.body.inventory;
                temporaryEquipment = context.body.GetComponent<TemporaryEquipment>();
                if (temporaryEquipment != null && (temporaryEquipment.realEquipmentStateSlots == null || temporaryEquipment.temporaryEquipmentStateSlots == null))
                {
                    temporaryEquipment = null;
                }
                if (temporaryEquipment != null)
                {
                    Helper.SwapEquipmentSlots(inventory, temporaryEquipment.realEquipmentStateSlots);
                }
            }

            public void Dispose()
            {
                if (temporaryEquipment != null)
                {
                    Helper.SwapEquipmentSlots(inventory, temporaryEquipment.temporaryEquipmentStateSlots);
                }
            }
        }

        public void SetTemporaryEquipment(EquipmentIndex equipmentIndex)
        {
            if (TryGetComponent(out CharacterBody body))
            {
                if (body.inventory != null && body.inventory._equipmentSlots != null && body.master != null)
                {
                    int maxEquipmentSlots = Helper.IsToolbotWithSwapSkill(body.master) ? 2 : 1;
                    int maxEquipmentSets = body.inventory.GetItemCountEffective(DLC3Content.Items.ExtraEquipment.itemIndex) + 1;
                    for (uint slot = 0; slot < maxEquipmentSlots; slot++)
                    {
                        for (uint set = 0; set < maxEquipmentSets; set++)
                        {
                            body.inventory.SetEquipmentIndex(equipmentIndex, new EquipmentLocation { slot = slot, set = set });
                        }
                    }
                    temporaryEquipmentStateSlots = Helper.CopyEquipmentState(body.inventory._equipmentSlots);
                }
            }
        }

        private void OnEnable()
        {
            if (TryGetComponent(out CharacterBody body))
            {
                if(body.inventory != null && body.inventory._equipmentSlots != null) {
                    snapshotInventory = body.inventory;
                    if (!snapshots.TryGetValue(snapshotInventory, out var snapshot))
                    {
                        snapshot = new Snapshot { real = Helper.CopyEquipmentState(snapshotInventory._equipmentSlots) };
                        snapshots[snapshotInventory] = snapshot;
                    }
                    snapshot.users++;
                    // shared, so equipment picked up during the event lands in it
                    realEquipmentStateSlots = snapshot.real;
                }
            }
        }

        private void OnDisable()
        {
            var inventory = snapshotInventory;
            snapshotInventory = null;
            if (inventory is null || !snapshots.TryGetValue(inventory, out var snapshot))
            {
                return;
            }
            snapshot.users--;
            if (snapshot.users > 0)
            {
                // another body of this player still has the event's equipment
                return;
            }
            snapshots.Remove(inventory);
            if (!inventory)
            {
                return;
            }
            // every slot and set this event may have written; the snapshot's own if this body never got the equipment
            var changedEquipmentStateSlots = temporaryEquipmentStateSlots ?? realEquipmentStateSlots;
            for (uint slot = 0; slot < changedEquipmentStateSlots.Length; slot++)
            {
                for (uint set = 0; set < changedEquipmentStateSlots[slot]._setCount; set++)
                {
                    var location = new EquipmentLocation { slot = slot, set = set };
                    if(slot < realEquipmentStateSlots.Length && set < realEquipmentStateSlots[slot]._setCount)
                    {
                        inventory.SetEquipment(realEquipmentStateSlots[slot].GetEquipment(set), location);
                    }
                    else
                    {
                        inventory.SetEquipmentIndex(EquipmentIndex.None, location);
                    }
                }
            }
        }
    }
}
