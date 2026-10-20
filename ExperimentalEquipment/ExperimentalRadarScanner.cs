using RoR2;
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
        private const float ScannerWaveSpeed = 100f;
        private const float ScannerWaveRadius = 100f;
        // per monster the wave reaches, as a multiple of the player's damage
        private const float ScannerWaveDamage = 25f;
        // each lowers the maximum health, for good
        private const int CurseStacks = 10;
        private static AsyncOperationHandle<GameObject> WaveEffect;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Scanner;
        public override float CooldownScale => 0.25f;
        public override string Description => "Radar Scanner sends out a shockwave instead of scanning, which hurts, stuns, curses and throws back monsters";
        public override string PopupDescription => "Sends out a shockwave instead of scanning, which hurts, stuns, curses and throws back monsters.";

        public override void Preload()
        {
            // a Jellyfish's nova, as big as the wave's reach
            WaveEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Jellyfish/JellyfishNova.prefab");
        }

        // a shockwave instead of the scan
        public override bool Fire(EquipmentSlot slot)
        {
            new GameObject("RandomEventsScannerWave").AddComponent<ScannerWave>().attacker = slot.characterBody;
            return true;
        }

        // The experimental Radar Scanner's shockwave, shown by a Jellyfish's nova, hurts, stuns, curses and throws back each
        // monster it reaches
        public class ScannerWave : MonoBehaviour
        {
            public CharacterBody attacker;
            private Vector3 origin;
            private float radius;
            private readonly HashSet<CharacterBody> hit = new HashSet<CharacterBody>();

            private void Start()
            {
                origin = attacker != null ? attacker.corePosition : transform.position;
                EffectManager.SpawnEffect(WaveEffect.WaitForCompletion(), new EffectData { origin = origin, scale = ScannerWaveRadius }, transmit: true);
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
                    body.SetBuffCount(RoR2Content.Buffs.PermanentCurse.buffIndex, body.GetBuffCount(RoR2Content.Buffs.PermanentCurse) + CurseStacks);
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
