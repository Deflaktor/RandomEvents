using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class ExperimentalForeignFruit : ExperimentalEquipment
    {
        private const float Duration = 15f;
        // Growth Nectar's buff, 4% more of every stat per stack, about half again with 13
        private const int StatStacks = 13;
        // 1% of the size per stack
        private const int SizeStacks = 50;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Fruit;
        public override string Description => "Foreign Fruit fully heals you and makes you half again as big and strong for 15 seconds";
        public override string PopupDescription => "Fully heals you and makes you half again as big and strong for 15 seconds.";

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
            // eaten again while it lasts, it lasts longer, but grows no more
            body.ClearTimedBuffs(DLC2Content.Buffs.BoostAllStatsBuff);
            for (int i = 0; i < StatStacks; i++)
            {
                body.AddTimedBuff(DLC2Content.Buffs.BoostAllStatsBuff, Duration);
            }
            var growth = body.GetComponent<Growth>() ?? body.gameObject.AddComponent<Growth>();
            growth.timeLeft = Duration;
            return true;
        }

        // The size items, which every player's game sees in the inventory, taken back when it wears off
        public class Growth : MonoBehaviour
        {
            public float timeLeft;
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
