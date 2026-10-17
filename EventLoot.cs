using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Items an event drops, which their finder keeps only until the event ends. The mark follows the item from its
    // droplet to its pickup, also through the Artifact of Command's choice cube.
    public static class EventLoot
    {
        private static readonly Dictionary<AbstractEvent, List<TemporaryInventory>> inventories = new Dictionary<AbstractEvent, List<TemporaryInventory>>();
        private static readonly List<EventLootMarker> markers = new List<EventLootMarker>();
        // the event whose droplet or choice cube is creating a pickup right now
        private static AbstractEvent creatingFor;

        public static void Hook()
        {
            On.RoR2.PickupDropletController.CreatePickup += PickupDropletController_CreatePickup;
            On.RoR2.PickupPickerController.SetOptionsFromPickupForCommandArtifact_UniquePickup += PickupPickerController_SetOptionsFromPickupForCommandArtifact;
            On.RoR2.PickupPickerController.CreatePickup_UniquePickup += PickupPickerController_CreatePickup;
            On.RoR2.GenericPickupController.CreatePickup += GenericPickupController_CreatePickup;
            On.RoR2.GenericPickupController.AttemptGrant += GenericPickupController_AttemptGrant;
        }

        public static void Drop(AbstractEvent owner, PickupIndex pickupIndex, Vector3 position, Vector3 velocity)
        {
            PickupDropletController.CreatePickupDroplet(new UniquePickup { pickupIndex = pickupIndex }, position, velocity, false);
            // the droplet just created is the last one tracked
            var droplets = InstanceTracker.GetInstancesList<PickupDropletController>();
            if (droplets.Count > 0)
            {
                Mark(droplets[droplets.Count - 1].gameObject, owner);
            }
        }

        // the inventory of the event's items for the body; other temporary things of the event can go there too
        public static TemporaryInventory GetInventory(AbstractEvent owner, CharacterBody body)
        {
            if (!inventories.TryGetValue(owner, out var list))
            {
                list = new List<TemporaryInventory>();
                inventories[owner] = list;
            }
            var inventory = TemporaryInventory.Find(body.gameObject, list);
            if (inventory == null)
            {
                inventory = body.gameObject.AddComponent<TemporaryInventory>();
                list.Add(inventory);
            }
            return inventory;
        }

        // takes the event's items back, along with what nobody picked up
        public static void Clear(AbstractEvent owner)
        {
            if (inventories.TryGetValue(owner, out var list))
            {
                foreach (var inventory in list)
                {
                    if (inventory != null)
                    {
                        Object.Destroy(inventory);
                    }
                }
                inventories.Remove(owner);
            }
            foreach (var marker in markers.Where(marker => marker != null && marker.owner == owner).ToList())
            {
                Object.Destroy(marker.gameObject);
            }
        }

        private static void Mark(GameObject obj, AbstractEvent owner)
        {
            var marker = obj.AddComponent<EventLootMarker>();
            marker.owner = owner;
            markers.Add(marker);
        }

        private static AbstractEvent GetOwner(Component component)
        {
            return component.TryGetComponent(out EventLootMarker marker) ? marker.owner : null;
        }

        private static void PickupDropletController_CreatePickup(On.RoR2.PickupDropletController.orig_CreatePickup orig, PickupDropletController self)
        {
            creatingFor = GetOwner(self);
            try
            {
                orig(self);
            }
            finally
            {
                creatingFor = null;
            }
        }

        // the Artifact of Command turns the droplet into a choice cube
        private static void PickupPickerController_SetOptionsFromPickupForCommandArtifact(On.RoR2.PickupPickerController.orig_SetOptionsFromPickupForCommandArtifact_UniquePickup orig, PickupPickerController self, UniquePickup pickup)
        {
            orig(self, pickup);
            if (creatingFor != null)
            {
                Mark(self.gameObject, creatingFor);
            }
        }

        private static void PickupPickerController_CreatePickup(On.RoR2.PickupPickerController.orig_CreatePickup_UniquePickup orig, PickupPickerController self, UniquePickup pickupState)
        {
            creatingFor = GetOwner(self);
            try
            {
                orig(self, pickupState);
            }
            finally
            {
                creatingFor = null;
            }
        }

        private static GenericPickupController GenericPickupController_CreatePickup(On.RoR2.GenericPickupController.orig_CreatePickup orig, ref GenericPickupController.CreatePickupInfo createPickupInfo)
        {
            var pickup = orig(ref createPickupInfo);
            if (creatingFor != null && pickup != null)
            {
                Mark(pickup.gameObject, creatingFor);
            }
            return pickup;
        }

        // the item goes into the finder's inventory of the event's items instead
        private static void GenericPickupController_AttemptGrant(On.RoR2.GenericPickupController.orig_AttemptGrant orig, GenericPickupController self, CharacterBody body)
        {
            var owner = GetOwner(self);
            var itemIndex = PickupCatalog.GetPickupDef(self.pickup.pickupIndex)?.itemIndex ?? ItemIndex.None;
            if (!NetworkServer.active || owner == null || !owner.IsActive() || body == null || body.inventory == null || itemIndex == ItemIndex.None)
            {
                orig(self, body);
                return;
            }
            var before = body.inventory.GetItemCountPermanent(itemIndex);
            orig(self, body);
            var granted = body.inventory.GetItemCountPermanent(itemIndex) - before;
            if (granted > 0)
            {
                body.inventory.RemoveItemPermanent(itemIndex, granted);
                GetInventory(owner, body).GiveTemporaryItem(itemIndex, granted);
            }
        }

        private class EventLootMarker : MonoBehaviour
        {
            public AbstractEvent owner;

            private void OnDestroy()
            {
                markers.Remove(this);
            }
        }
    }
}
