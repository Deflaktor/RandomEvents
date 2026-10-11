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
        private Inventory.EquipmentStateSlot[] realEquipmentStateSlots;
        private Inventory.EquipmentStateSlot[] temporaryEquipmentStateSlots;
        public float cooldownScale = 1f;
        public bool doNotDisableEquipment = false;

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

        private static void Inventory_SetEquipmentDisabled(On.RoR2.Inventory.orig_SetEquipmentDisabled orig, Inventory self, bool _active)
        {
            if (self.TryGetComponent<CharacterMaster>(out var characterMaster))
            {
                if (characterMaster.bodyInstanceObject != null)
                {
                    if (characterMaster.bodyInstanceObject.TryGetComponent<CharacterBody>(out var characterBody) && characterMaster.bodyInstanceObject.TryGetComponent<TemporaryEquipment>(out var temporaryEquipment))
                    {
                        if (temporaryEquipment.doNotDisableEquipment)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    Log.LogError($"characterMaster.bodyInstanceObject is null: {characterMaster}");
                }
            }
            else
            {
                Log.LogError($"Inventory does not have a CharacterMaster: {self}");
            }
            orig(self, _active);
        }

        private static float Inventory_CalculateEquipmentCooldownScale(On.RoR2.Inventory.orig_CalculateEquipmentCooldownScale orig, Inventory self)
        {
            if (self.TryGetComponent<CharacterMaster>(out var characterMaster))
            {
                if (characterMaster.bodyInstanceObject != null)
                {
                    if (characterMaster.bodyInstanceObject.TryGetComponent<CharacterBody>(out var characterBody) && characterMaster.bodyInstanceObject.TryGetComponent<TemporaryEquipment>(out var temporaryEquipment))
                    {
                        return temporaryEquipment.cooldownScale * orig(self);
                    }
                }
                else
                {
                    Log.LogError($"characterMaster.bodyInstanceObject is null: {characterMaster}");
                }
            }
            else
            {
                Log.LogError($"Inventory does not have a CharacterMaster: {self}");
            }
            return orig(self);
        }

        public class TemporarilyRemoveEquipment : IDisposable
        {
            Inventory inventory;
            TemporaryEquipment temporaryEquipment;

            public TemporarilyRemoveEquipment(Interactor activator)
            {
                inventory = activator.GetComponent<CharacterBody>().inventory;
                temporaryEquipment = activator.GetComponent<TemporaryEquipment>();
                if (temporaryEquipment != null)
                {
                    Helper.FillEquipment(inventory, EquipmentIndex.None);
                }
            }

            public void Dispose()
            {
                if (temporaryEquipment != null)
                {
                    Helper.TransferEquipmentIndex(temporaryEquipment.temporaryEquipmentStateSlots, inventory._equipmentSlots);
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
                    realEquipmentStateSlots = Helper.CopyEquipmentState(body.inventory._equipmentSlots);
                }
            }
        }

        private void OnDisable()
        {
            if (TryGetComponent(out CharacterBody body))
            {
                if (body.inventory != null)
                {
                    for (uint slot = 0; slot < temporaryEquipmentStateSlots.Length; slot++)
                    {
                        for (uint set = 0; set < temporaryEquipmentStateSlots[slot]._setCount; set++)
                        {
                            var location = new EquipmentLocation { slot = slot, set = set };
                            if(slot < realEquipmentStateSlots.Length && set < realEquipmentStateSlots[slot]._setCount)
                            {
                                body.inventory.SetEquipment(realEquipmentStateSlots[slot].GetEquipment(set), location);
                            }
                            else
                            {
                                body.inventory.SetEquipmentIndex(EquipmentIndex.None, location);
                            }
                        }
                    }
                }
            }
        }
    }
}
