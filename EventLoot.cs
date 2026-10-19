using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Items an event drops, which their finder keeps only until the event ends. The mark follows the item from its
    // droplet to its pickup, also through the Artifact of Command's choice cube. Monsters can be allowed to pick the
    // items up too, which they don't drop again.
    public static class EventLoot
    {
        private static readonly Dictionary<AbstractEvent, List<TemporaryInventory>> inventories = new Dictionary<AbstractEvent, List<TemporaryInventory>>();
        private static readonly List<EventLootMarker> markers = new List<EventLootMarker>();
        // the mark of the droplet or choice cube creating a pickup right now
        private static EventLootMarker creatingFrom;

        public static void Hook()
        {
            On.RoR2.PickupDropletController.CreatePickup += PickupDropletController_CreatePickup;
            On.RoR2.PickupPickerController.SetOptionsFromPickupForCommandArtifact_UniquePickup += PickupPickerController_SetOptionsFromPickupForCommandArtifact;
            On.RoR2.PickupPickerController.CreatePickup_UniquePickup += PickupPickerController_CreatePickup;
            On.RoR2.GenericPickupController.CreatePickup += GenericPickupController_CreatePickup;
            On.RoR2.GenericPickupController.AttemptGrant += GenericPickupController_AttemptGrant;
            // ShareSuite would give the event's items to everyone, for good
            if (ModCompatibilityShareSuite.enabled)
            {
                ModCompatibilityShareSuite.AddPickupEventHandler((pickup, picker) => GetMarker(pickup) == null);
            }
        }

        public static void Drop(AbstractEvent owner, PickupIndex pickupIndex, Vector3 position, Vector3 velocity, bool monstersCanPickUp = false)
        {
            // it looks like a temporary item, which it is
            PickupDropletController.CreatePickupDroplet(new UniquePickup { pickupIndex = pickupIndex, decayValue = 1f }, position, velocity, false);
            // the droplet just created is the last one tracked
            var droplets = InstanceTracker.GetInstancesList<PickupDropletController>();
            if (droplets.Count > 0)
            {
                Mark(droplets[droplets.Count - 1].gameObject, owner, monstersCanPickUp);
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

        private static void Mark(GameObject obj, AbstractEvent owner, bool monstersCanPickUp)
        {
            var marker = obj.AddComponent<EventLootMarker>();
            marker.owner = owner;
            marker.monstersCanPickUp = monstersCanPickUp;
            marker.createdTime = Time.fixedTime;
            markers.Add(marker);
        }

        // the event's items lying on the ground, with how long each has been there
        public static List<(GenericPickupController pickup, float age)> GetPickups(AbstractEvent owner)
        {
            var result = new List<(GenericPickupController pickup, float age)>();
            foreach (var marker in markers)
            {
                if (marker != null && marker.owner == owner && !marker.taken && marker.TryGetComponent(out GenericPickupController pickup))
                {
                    result.Add((pickup, Time.fixedTime - marker.createdTime));
                }
            }
            return result;
        }

        // A monster takes the item, which it keeps until the event ends; the game only lets players pick items up.
        public static bool GiveToMonster(GenericPickupController pickup, CharacterBody body)
        {
            var marker = GetMarker(pickup);
            var itemIndex = PickupCatalog.GetPickupDef(pickup.pickup.pickupIndex)?.itemIndex ?? ItemIndex.None;
            if (marker == null || !marker.monstersCanPickUp || marker.taken || body == null || body.inventory == null || itemIndex == ItemIndex.None)
            {
                return false;
            }
            marker.taken = true;
            GetInventory(marker.owner, body).GiveTemporaryItem(itemIndex);
            ItemOrb.FlySilently(pickup.transform.position, body.gameObject, itemIndex);
            Object.Destroy(pickup.gameObject);
            return true;
        }

        private static EventLootMarker GetMarker(Component component)
        {
            return component.TryGetComponent(out EventLootMarker marker) ? marker : null;
        }

        private static void PickupDropletController_CreatePickup(On.RoR2.PickupDropletController.orig_CreatePickup orig, PickupDropletController self)
        {
            creatingFrom = GetMarker(self);
            try
            {
                orig(self);
            }
            finally
            {
                creatingFrom = null;
            }
        }

        // the Artifact of Command turns the droplet into a choice cube
        private static void PickupPickerController_SetOptionsFromPickupForCommandArtifact(On.RoR2.PickupPickerController.orig_SetOptionsFromPickupForCommandArtifact_UniquePickup orig, PickupPickerController self, UniquePickup pickup)
        {
            orig(self, pickup);
            if (creatingFrom != null)
            {
                Mark(self.gameObject, creatingFrom.owner, creatingFrom.monstersCanPickUp);
            }
        }

        private static void PickupPickerController_CreatePickup(On.RoR2.PickupPickerController.orig_CreatePickup_UniquePickup orig, PickupPickerController self, UniquePickup pickupState)
        {
            creatingFrom = GetMarker(self);
            try
            {
                orig(self, pickupState);
            }
            finally
            {
                creatingFrom = null;
            }
        }

        private static GenericPickupController GenericPickupController_CreatePickup(On.RoR2.GenericPickupController.orig_CreatePickup orig, ref GenericPickupController.CreatePickupInfo createPickupInfo)
        {
            var pickup = orig(ref createPickupInfo);
            if (creatingFrom != null && pickup != null)
            {
                Mark(pickup.gameObject, creatingFrom.owner, creatingFrom.monstersCanPickUp);
            }
            return pickup;
        }

        // the item goes into the finder's inventory of the event's items instead
        private static void GenericPickupController_AttemptGrant(On.RoR2.GenericPickupController.orig_AttemptGrant orig, GenericPickupController self, CharacterBody body)
        {
            var marker = GetMarker(self);
            var owner = marker != null ? marker.owner : null;
            var itemIndex = PickupCatalog.GetPickupDef(self.pickup.pickupIndex)?.itemIndex ?? ItemIndex.None;
            if (!NetworkServer.active || owner == null || !owner.IsActive() || body == null || body.inventory == null || itemIndex == ItemIndex.None)
            {
                orig(self, body);
                return;
            }
            if (body.teamComponent.teamIndex != TeamIndex.Player)
            {
                GiveToMonster(self, body);
                return;
            }
            var pickupState = self.pickup;
            var before = body.inventory.GetItemCountPermanent(itemIndex);
            orig(self, body);
            var granted = body.inventory.GetItemCountPermanent(itemIndex) - before;
            if (granted > 0)
            {
                body.inventory.RemoveItemPermanent(itemIndex, granted);
                GetInventory(owner, body).GiveTemporaryItem(itemIndex, granted);
            }
            // a pickup that looks temporary gives the game's temporary item, which would decay and outlast the event
            else if (pickupState.isTempItem && self.consumed)
            {
                body.inventory.RemoveItemTemp(itemIndex, pickupState.decayValue);
                GetInventory(owner, body).GiveTemporaryItem(itemIndex);
            }
        }

        private class EventLootMarker : MonoBehaviour
        {
            public AbstractEvent owner;
            public bool monstersCanPickUp;
            public float createdTime;
            // a monster took the item, which the game destroys only at the end of the frame
            public bool taken;

            private void OnDestroy()
            {
                markers.Remove(this);
            }
        }
    }
}
