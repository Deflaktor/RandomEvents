using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class ExperimentalForeignFruit : ExperimentalEquipment
    {
        private const float Duration = 15f;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Fruit;
        public override string Description => "Foreign Fruit fully heals you and for 15 seconds doubles your damage and health, and makes you faster, tougher, bigger and regenerate, with a Hunter's Harpoon, each one eaten meanwhile adding more and doubling the damage again";
        public override string PopupDescription => "Fully heals you and makes you much stronger for 15 seconds. Eat more to stack it!";

        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (body.healthComponent == null || body.inventory == null)
            {
                return false;
            }
            var effectData = new EffectData { origin = body.transform.position };
            effectData.SetNetworkedObjectReference(body.gameObject);
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/FruitHealEffect"), effectData, transmit: true);
            body.healthComponent.HealFraction(1f, default);
            var growth = body.gameObject.AddComponent<Growth>();
            growth.timeLeft = Duration;
            growth.nth = body.GetComponents<Growth>().Length;
            return true;
        }

        // What one fruit gives, taken back when it wears off on its own. All of it is in the inventory or buffs, which every
        // player's game sees and works out the stats from. The items are temporary stacks, which can't be scrapped and
        // which a copy of the inventory, e.g. an Engineer's turret's, doesn't take along. With the nth fruit lasting:
        // damage 200%, 400%, 800%, ...; health 200%, 400%, 600%, ...; movement speed 150%, 200%, 250%, ...;
        // armor +40, +80, +120, ...; size 150%, 200%, 250%, ...; 2.5% of the health regenerated per second and a Hunter's
        // Harpoon for each.
        public class Growth : MonoBehaviour
        {
            // the hidden items, 10% more damage and health per stack
            private const int DamageStacks = 10;
            private const int FirstHealthStacks = 10;
            private const int HealthStacks = 20;
            // Hunter's Harpoon's buff, 25% faster per stack, given untimed so its kills don't clear it
            private const int SpeedStacks = 2;
            // +20 armor per stack
            private const int ArmorStacks = 2;
            // 2.5% of the health per second per stack
            private const int RegenStacks = 1;
            // 1% of the size per stack
            private const int SizeStacks = 50;
            private const int Harpoons = 1;
            // until the new health counts, so the heal fills it
            private const float HealDelay = 0.2f;
            public float timeLeft;
            public int nth;
            private CharacterBody body;
            // the master's, which outlives the body
            private Inventory inventory;
            private int damageStacks;
            private int healthStacks;
            private float healTimer = HealDelay;

            private void Start()
            {
                body = GetComponent<CharacterBody>();
                damageStacks = DamageStacks << Mathf.Min(nth - 1, 20);
                healthStacks = nth == 1 ? FirstHealthStacks : HealthStacks;
                inventory = body.inventory;
                inventory.GiveItemChanneled(RoR2Content.Items.BoostDamage.itemIndex, damageStacks);
                inventory.GiveItemChanneled(RoR2Content.Items.BoostHp.itemIndex, healthStacks);
                inventory.GiveItemChanneled(DLC4Content.Items.BoostCharacterScale.itemIndex, SizeStacks);
                inventory.GiveItemChanneled(DLC1Content.Items.MoveSpeedOnKill.itemIndex, Harpoons);
                AddBuff(DLC1Content.Buffs.KillMoveSpeed, SpeedStacks);
                AddBuff(DLC4Content.Buffs.MinorArmorBuff, ArmorStacks);
                AddBuff(DLC3Content.Buffs.HealNovaRegen, RegenStacks);
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                if (healTimer > 0f)
                {
                    healTimer -= Time.fixedDeltaTime;
                    if (healTimer <= 0f && body != null && body.healthComponent != null && body.healthComponent.alive)
                    {
                        body.healthComponent.HealFraction(1f, default);
                    }
                }
                timeLeft -= Time.fixedDeltaTime;
                if (timeLeft <= 0f)
                {
                    Destroy(this);
                }
            }

            private void AddBuff(BuffDef buffDef, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    body.AddBuff(buffDef);
                }
            }

            private void RemoveBuff(BuffDef buffDef, int count)
            {
                for (int i = 0; i < count && body.HasBuff(buffDef); i++)
                {
                    body.RemoveBuff(buffDef);
                }
            }

            private void OnDestroy()
            {
                if (inventory != null)
                {
                    inventory.RemoveItemChanneled(RoR2Content.Items.BoostDamage.itemIndex, damageStacks);
                    inventory.RemoveItemChanneled(RoR2Content.Items.BoostHp.itemIndex, healthStacks);
                    inventory.RemoveItemChanneled(DLC4Content.Items.BoostCharacterScale.itemIndex, SizeStacks);
                    inventory.RemoveItemChanneled(DLC1Content.Items.MoveSpeedOnKill.itemIndex, Harpoons);
                }
                if (body == null)
                {
                    return;
                }
                RemoveBuff(DLC1Content.Buffs.KillMoveSpeed, SpeedStacks);
                RemoveBuff(DLC4Content.Buffs.MinorArmorBuff, ArmorStacks);
                RemoveBuff(DLC3Content.Buffs.HealNovaRegen, RegenStacks);
            }
        }
    }
}
