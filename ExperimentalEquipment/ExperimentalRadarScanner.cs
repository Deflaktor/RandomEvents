using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class ExperimentalRadarScanner : ExperimentalEquipment
    {
        // out to 200 meters in 2.5 seconds, as long as Mithrix's shockwaves show anything
        private const float ScannerWaveSpeed = 80f;
        private const float ScannerWaveRadius = 200f;
        // per monster the wave reaches, as a multiple of the player's damage
        private const float ScannerWaveDamage = 25f;
        // Mithrix's shockwaves, this many in a ring, running out with the wave
        private const int WaveEffects = 24;
        private static AsyncOperationHandle<GameObject> WaveEffect;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Scanner;
        public override float CooldownScale => 0.25f;
        public override string Description => "Radar Scanner sends out a shockwave instead of scanning, which hurts, stuns and throws back monsters";
        public override string PopupDescription => "Sends out a shockwave instead of scanning, which hurts, stuns and knocks back monsters.";

        public override void Preload()
        {
            WaveEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Brother/BrotherSunderWave.prefab");
        }

        // a shockwave instead of the scan
        public override bool Fire(EquipmentSlot slot)
        {
            new GameObject("RandomEventsScannerWave").AddComponent<ScannerWave>().attacker = slot.characterBody;
            return true;
        }

        // The experimental Radar Scanner's shockwave, shown by a ring of Mithrix's shockwaves running along the ground, hurts,
        // stuns and throws back each monster it reaches
        public class ScannerWave : MonoBehaviour
        {
            public CharacterBody attacker;
            private Vector3 origin;
            private float radius;
            private readonly HashSet<CharacterBody> hit = new HashSet<CharacterBody>();

            private void Start()
            {
                origin = attacker != null ? attacker.corePosition : transform.position;
                var ground = Physics.Raycast(origin, Vector3.down, out var hit, 50f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : origin;
                for (int i = 0; i < WaveEffects; i++)
                {
                    SpawnWave(ground, Quaternion.Euler(0f, 360f * i / WaveEffects, 0f));
                }
            }

            // only for show: it hurts nothing, and the host moves it as fast and as far as the wave goes
            private void SpawnWave(Vector3 position, Quaternion rotation)
            {
                var wave = Instantiate(WaveEffect.WaitForCompletion(), position, rotation);
                if (wave.TryGetComponent(out ProjectileOverlapAttack overlapAttack))
                {
                    overlapAttack.enabled = false;
                }
                if (wave.TryGetComponent(out ProjectileDamage projectileDamage))
                {
                    projectileDamage.damage = 0f;
                }
                if (wave.TryGetComponent(out ProjectileCharacterController mover))
                {
                    mover.velocity = ScannerWaveSpeed;
                    mover.lifetime = ScannerWaveRadius / ScannerWaveSpeed;
                }
                if (wave.TryGetComponent(out ProjectileController controller) && attacker != null)
                {
                    controller.owner = attacker.gameObject;
                }
                if (wave.TryGetComponent(out TeamFilter teamFilter) && attacker != null)
                {
                    teamFilter.teamIndex = attacker.teamComponent.teamIndex;
                }
                NetworkServer.Spawn(wave);
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || attacker == null)
                {
                    Destroy(gameObject);
                    return;
                }
                radius += ScannerWaveSpeed * Time.fixedDeltaTime;
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (body == null || hit.Contains(body) || body.healthComponent == null || !body.healthComponent.alive
                        || body.teamComponent.teamIndex == attacker.teamComponent.teamIndex || body.teamComponent.teamIndex == TeamIndex.Neutral)
                    {
                        continue;
                    }
                    var offset = body.corePosition - origin;
                    if (offset.sqrMagnitude > radius * radius)
                    {
                        continue;
                    }
                    hit.Add(body);
                    var direction = new Vector3(offset.x, 0f, offset.z).normalized;
                    var mass = body.characterMotor != null ? body.characterMotor.mass : body.rigidbody != null ? body.rigidbody.mass : 100f;
                    body.healthComponent.TakeDamage(new DamageInfo
                    {
                        attacker = attacker.gameObject,
                        damage = attacker.damage * ScannerWaveDamage,
                        crit = attacker.RollCrit(),
                        position = body.corePosition,
                        force = (direction * 60f + Vector3.up * 30f) * Mathf.Min(mass, 1000f),
                        // stunned for 5 seconds, like by the Captain's Tazer
                        damageType = DamageType.Shock5s,
                        procCoefficient = 1f,
                        damageColorIndex = DamageColorIndex.Item
                    });
                }
                if (radius >= ScannerWaveRadius)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
