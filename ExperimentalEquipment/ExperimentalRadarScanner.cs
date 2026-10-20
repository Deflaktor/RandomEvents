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
        private const float ScannerWaveRingInterval = 0.1f;
        // per monster the wave reaches, as a multiple of the player's damage
        private const float ScannerWaveDamage = 25f;
        // each lowers the maximum health, for good
        private const int CurseStacks = 10;
        private static AsyncOperationHandle<GameObject> ShockNova;
        private static AsyncOperationHandle<GameObject> ShockRing;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Scanner;
        public override float CooldownScale => 0.25f;
        public override string Description => "Radar Scanner sends out a shockwave instead of scanning, which hurts, stuns, curses and throws back monsters";
        public override string PopupDescription => "Sends out a shockwave instead of scanning, which hurts, stuns, curses and throws back monsters.";

        public override void Preload()
        {
            // the Captain's shocking beacon's nova where it starts, his Tazer's for the rings it spreads in
            ShockNova = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainTazerSupplyDropNova.prefab");
            ShockRing = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainTazerNova.prefab");
        }

        // a shockwave instead of the scan
        public override bool Fire(EquipmentSlot slot)
        {
            new GameObject("RandomEventsScannerWave").AddComponent<ScannerWave>().attacker = slot.characterBody;
            return true;
        }

        // The experimental Radar Scanner's shockwave, spreading in rings of the Captain's Tazer, hurts, stuns, curses and throws back
        // each monster it reaches
        public class ScannerWave : MonoBehaviour
        {
            public CharacterBody attacker;
            private Vector3 origin;
            private float radius;
            private readonly HashSet<CharacterBody> hit = new HashSet<CharacterBody>();

            private float ringTimer;

            private void Start()
            {
                origin = attacker != null ? attacker.corePosition : transform.position;
                EffectManager.SpawnEffect(ShockNova.WaitForCompletion(), new EffectData { origin = origin, scale = 15f }, transmit: true);
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || attacker == null)
                {
                    Destroy(gameObject);
                    return;
                }
                radius += ScannerWaveSpeed * Time.fixedDeltaTime;
                ringTimer -= Time.fixedDeltaTime;
                if (ringTimer <= 0f)
                {
                    ringTimer = ScannerWaveRingInterval;
                    EffectManager.SpawnEffect(ShockRing.WaitForCompletion(), new EffectData { origin = origin, scale = radius }, transmit: true);
                }
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
