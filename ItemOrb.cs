using RoR2;
using RoR2.Orbs;
using UnityEngine;

namespace RandomEvents
{
    // Shows an item flying to a character. With the whoosh of an item being handed over, e.g. by a printer; silently
    // where many can fly at once, e.g. one for every kill. Only for show: the item is given separately.
    public static class ItemOrb
    {
        public static void Fly(Vector3 origin, GameObject target, ItemIndex itemIndex)
        {
            var hurtBox = target != null && target.TryGetComponent(out CharacterBody body) ? body.mainHurtBox : null;
            if (hurtBox == null)
            {
                FlySilently(origin, target, itemIndex);
                return;
            }
            OrbManager.instance.AddOrb(new ItemTransferOrb
            {
                origin = origin,
                target = hurtBox,
                itemIndex = itemIndex,
                onArrival = orb => { }
            });
        }

        public static void FlySilently(Vector3 origin, GameObject target, ItemIndex itemIndex)
        {
            PurchaseInteraction.CreateItemTakenOrb(origin, target, itemIndex);
        }
    }
}
