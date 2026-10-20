using RoR2;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class ExperimentalForeignFruit : ExperimentalEquipment
    {
        private const float Duration = 15f;
        // Growth Nectar's buff, 4% more of every stat per stack, about half again with 13; each fruit eaten while others
        // last doubles what they give
        private const int StatStacks = 13;
        // 1% of the size per stack, the same with each fruit
        private const int SizeStacks = 50;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Fruit;
        public override string Description => "Foreign Fruit fully heals you and makes you half again as big and strong for 15 seconds, each one eaten meanwhile adding as much size and doubling the strength";
        public override string PopupDescription => "Fully heals you and makes you half again as big and strong for 15 seconds. Each one eaten meanwhile adds as much size and doubles the strength.";

        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (body.healthComponent == null)
            {
                return false;
            }
            var effectData = new EffectData { origin = body.transform.position };
            effectData.SetNetworkedObjectReference(body.gameObject);
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/FruitHealEffect"), effectData, transmit: true);
            body.healthComponent.HealFraction(1f, default);
            // each fruit adds as much strength as those still lasting give together, which wears off on its own
            var lasting = body.GetComponents<Growth>();
            var growth = body.gameObject.AddComponent<Growth>();
            growth.timeLeft = Duration;
            growth.statStacks = Mathf.Max(StatStacks, lasting.Sum(other => other.statStacks));
            for (int i = 0; i < growth.statStacks; i++)
            {
                body.AddTimedBuff(DLC2Content.Buffs.BoostAllStatsBuff, Duration);
            }
            return true;
        }

        // One fruit's size items, which every player's game sees in the inventory, taken back when it wears off
        public class Growth : MonoBehaviour
        {
            public float timeLeft;
            public int statStacks;
            private Inventory inventory;

            private void Start()
            {
                inventory = GetComponent<CharacterBody>()?.inventory;
                inventory?.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, SizeStacks);
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                timeLeft -= Time.fixedDeltaTime;
                if (timeLeft <= 0f)
                {
                    Destroy(this);
                }
            }

            private void OnDestroy()
            {
                if (inventory != null)
                {
                    inventory.RemoveItemPermanent(DLC4Content.Items.BoostCharacterScale, SizeStacks);
                }
            }
        }
    }
}
