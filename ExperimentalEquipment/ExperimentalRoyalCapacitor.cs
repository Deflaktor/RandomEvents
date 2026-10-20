using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class ExperimentalRoyalCapacitor : ExperimentalEquipment
    {
        private const float InflateTime = 2f;
        private const float InflateFactor = 3f;
        private static AsyncOperationHandle<GameObject> Breach;
        private static AsyncOperationHandle<GameObject> Tick;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Lightning;
        public override string Description => "Royal Capacitor inflates a monster until it explodes";
        public override string PopupDescription => "Inflates the targeted monster to three times its size, then it explodes. No effect on bosses.";

        public override void Preload()
        {
            // the explosion of ExplodingCorpses' monsters
            Breach = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Scorchling/VFXScorchlingBreachExplosion.prefab");
            Tick = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab");
        }

        public override bool Fire(EquipmentSlot slot)
        {
            slot.UpdateTargets(RoR2Content.Equipment.Lightning.equipmentIndex, userShouldAnticipateTarget: true);
            var hurtBox = slot.currentTarget.hurtBox;
            var target = hurtBox != null && hurtBox.healthComponent != null ? hurtBox.healthComponent.body : null;
            if (target == null || target.isBoss || target.inventory == null || !target.healthComponent.alive || target.GetComponent<Inflation>() != null)
            {
                return false;
            }
            slot.subcooldownTimer = 0.2f;
            slot.InvalidateCurrentTarget();
            target.gameObject.AddComponent<Inflation>().attacker = slot.characterBody;
            return true;
        }


        // The target grows to three times its size in 2 seconds, faster and faster ticking, then explodes like
        // ExplodingCorpses' monsters, the bigger the bigger it is
        public class Inflation : MonoBehaviour
        {
            public CharacterBody attacker;
            private CharacterBody body;
            private int startPercent;
            private int added;
            private float time;
            private float tickTimer;

            // The game grows a body to a new size evenly over 2 seconds, starting over with each change. So all the size
            // items come at once, and the growth itself takes the 2 seconds, on every player's screen alike.
            private void Start()
            {
                body = GetComponent<CharacterBody>();
                startPercent = 100 + body.inventory.GetItemCountEffective(DLC4Content.Items.BoostCharacterScale) - body.inventory.GetItemCountEffective(DLC4Content.Items.ReduceCharacterScale);
                added = Mathf.RoundToInt(startPercent * (InflateFactor - 1f));
                body.inventory.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, added);
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || body == null)
                {
                    return;
                }
                time += Time.fixedDeltaTime;
                var progress = Mathf.Clamp01(time / InflateTime);
                tickTimer -= Time.fixedDeltaTime;
                if (tickTimer <= 0f)
                {
                    tickTimer = Mathf.Lerp(0.5f, 0.08f, progress);
                    EffectManager.SpawnEffect(Tick.WaitForCompletion(), new EffectData
                    {
                        origin = body.corePosition,
                        scale = body.radius * (1f + progress)
                    }, transmit: true);
                }
                // once it has grown to its full size, with a little leeway in case it never quite gets there
                var grown = body.currentScale >= body.desiredScale - 0.01f && time >= InflateTime * 0.5f;
                if (grown || time >= InflateTime + 0.5f || !body.healthComponent.alive)
                {
                    Explode();
                }
            }

            private void Explode()
            {
                var scale = (startPercent + added) / 100f;
                var position = Vector3.Lerp(body.corePosition, body.footPosition, 0.5f);
                var radius = Mathf.Max(10f, Mathf.Sqrt(Mathf.Min(body.baseMaxHealth, 5000f)) * 0.7f) * scale / InflateFactor * 1.5f;
                EffectManager.SpawnEffect(Breach.WaitForCompletion(), new EffectData
                {
                    origin = position,
                    scale = radius * 0.75f
                }, transmit: true);
                if (attacker != null)
                {
                    new BlastAttack
                    {
                        position = position,
                        radius = radius,
                        falloffModel = BlastAttack.FalloffModel.SweetSpot,
                        attacker = attacker.gameObject,
                        baseDamage = attacker.damage * 10f,
                        baseForce = 3000f,
                        attackerFiltering = AttackerFiltering.NeverHitSelf,
                        crit = attacker.RollCrit(),
                        procCoefficient = 1f,
                        damageColorIndex = DamageColorIndex.Item,
                        teamIndex = attacker.teamComponent.teamIndex
                    }.Fire();
                }
                if (body.healthComponent.alive)
                {
                    // blown to pieces: its death leaves no body behind, on every player's screen
                    body.healthComponent.NetworkdestroyModelOnDeath = true;
                    body.healthComponent.Suicide(attacker != null ? attacker.gameObject : null);
                }
                Destroy(this);
            }
        }
    }
}
