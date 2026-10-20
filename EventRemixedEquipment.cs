using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventRemixedEquipment : AbstractEvent
    {
        private const float InflateTime = 2f;
        private const float InflateFactor = 3f;
        private const float ScannerWaveSpeed = 125f;
        private const float ScannerWaveRadius = 500f;
        // per monster the wave reaches, as a multiple of the player's damage
        private const float ScannerWaveDamage = 25f;
        // per missile, as a multiple of the player's damage
        private const float ShrimpMissileDamage = 30f;
        private const float DollSpeed = 6f;
        private const float DollFuse = 10f;
        private const float DollNukeRadius = 40f;
        private static AsyncOperationHandle<GameObject> Doll;
        private static AsyncOperationHandle<GameObject> Breach;
        private static AsyncOperationHandle<GameObject> Tick;
        private const float LeechDuration = 8f;
        private EquipmentDef equipmentDef;

        private static EquipmentDef[] Equipments => new[]
        {
            RoR2Content.Equipment.Lightning, RoR2Content.Equipment.CommandMissile, RoR2Content.Equipment.Scanner, RoR2Content.Equipment.LifestealOnHit,
            RoR2Content.Equipment.DeathProjectile
        };

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Artifact of Enigma would swap the equipment on every use, and Equipment Only gives its own
            var enigmaArtifactActive = RunArtifactManager.enabledArtifactsEnumerable.Any(e => e == RoR2Content.Artifacts.Enigma)
                || activeOtherEvents.Any(e => e is EventRandomArtifact randomArtifact && randomArtifact.TurnsOn(RoR2Content.Artifacts.Enigma));
            return !enigmaArtifactActive && !Helper.IsSimulacrumBossWaveNext()
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("EquipmentOnly", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "RemixedEquipment";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_REMIXED_EQUIPMENT", Language.GetString((equipmentDef ?? RoR2Content.Equipment.Lightning).nameToken));
        }
        public override string GetDescription()
        {
            return "Gives all players a remixed equipment with a shorter cooldown. Royal Capacitor inflates a monster until it explodes, Disposable Missile Launcher fires a Plasma Shrimp missile at every monster, Radar Scanner's wave also pushes and hurts monsters, and Super Massive Leech causes hemorrhage instead of healing, and Forgive Me Please's doll walks straight ahead and explodes like a mini nuke after 10 seconds.";
        }
        public override string GetPopupDescription()
        {
            if (equipmentDef == RoR2Content.Equipment.CommandMissile)
                return "Fires a Plasma Shrimp missile at every monster.";
            if (equipmentDef == RoR2Content.Equipment.Scanner)
                return "The wave also pushes and hurts monsters.";
            if (equipmentDef == RoR2Content.Equipment.LifestealOnHit)
                return "Your hits cause hemorrhage instead of healing you.";
            if (equipmentDef == RoR2Content.Equipment.DeathProjectile)
                return "The doll walks straight ahead and explodes like a mini nuke after 10 seconds.";
            return "Inflates the targeted monster to three times its size, then it explodes. No effect on bosses.";
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave, event \"EquipmentOnly\" inactive and Artifact of Enigma inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
            // the explosion of ExplodingCorpses' monsters
            Breach = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Scorchling/VFXScorchlingBreachExplosion.prefab");
            Tick = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab");
            Doll = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/DeathProjectile/DeathProjectile.prefab");
            EventHotPotato.MiniNuke.Preload();
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.EquipmentSlot.PerformEquipmentAction += EquipmentSlot_PerformEquipmentAction;
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                Give(body);
            }
        }

        private void Give(CharacterBody body)
        {
            if (body.inventory == null || equipmentDef == null || body.GetComponent<RemixComponent>() != null)
            {
                return;
            }
            body.gameObject.AddComponent<RemixComponent>().Setup(equipmentDef);
        }

        private bool IsRemixed(EquipmentSlot slot, EquipmentDef def)
        {
            return ModConfig.Enabled.Value && NetworkServer.active && IsActive() && def == equipmentDef && slot.characterBody != null
                && slot.characterBody.GetComponent<RemixComponent>() != null;
        }

        // returning false fires nothing and keeps the charge
        private bool EquipmentSlot_PerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot self, EquipmentDef def)
        {
            if (!IsRemixed(self, def) || self.equipmentDisabled)
            {
                return orig(self, def);
            }
            if (def == RoR2Content.Equipment.Lightning)
            {
                return Inflate(self);
            }
            if (def == RoR2Content.Equipment.CommandMissile)
            {
                return FireShrimpMissiles(self.characterBody);
            }
            if (def == RoR2Content.Equipment.Scanner)
            {
                var fired = orig(self, def);
                if (fired)
                {
                    new GameObject("RandomEventsScannerWave").AddComponent<ScannerWave>().attacker = self.characterBody;
                }
                return fired;
            }
            if (def == RoR2Content.Equipment.LifestealOnHit)
            {
                return Bleed(self.characterBody);
            }
            if (def == RoR2Content.Equipment.DeathProjectile)
            {
                return LaunchDoll(self);
            }
            return orig(self, def);
        }

        // Not the leech's own buff, which would heal on hit. This one does nothing itself, but shows a red glow
        private static bool Bleed(CharacterBody body)
        {
            var effectData = new EffectData
            {
                origin = body.corePosition
            };
            effectData.SetNetworkedObjectReference(body.gameObject);
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/LifeStealOnHitActivation"), effectData, transmit: true);
            body.AddTimedBuff(DLC3Content.Buffs.ShieldTankDamage, LeechDuration);
            return true;
        }

        // The doll, without its own behavior: no on-kill effects, no sticking, no physics. The host moves it, which its
        // projectile transform passes on to everyone. Each player can have 3 out at once, as with the original doll.
        private static bool LaunchDoll(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (body.master == null || body.master.IsDeployableLimited(DeployableSlot.DeathProjectile))
            {
                return false;
            }
            var aim = slot.GetAimRay().direction;
            var direction = new Vector3(aim.x, 0f, aim.z);
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : body.transform.forward;
            var doll = UnityEngine.Object.Instantiate(Doll.WaitForCompletion(), body.footPosition + direction * 1.5f + Vector3.up, Quaternion.LookRotation(direction));
            foreach (var behaviour in new Behaviour[] { doll.GetComponent<DeathProjectile>(), doll.GetComponent<ProjectileStickOnImpact>(), doll.GetComponent<ProjectileSimple>(),
                doll.GetComponent<ApplyTorqueOnStart>(), doll.GetComponent<DestroyOnTimer>() })
            {
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }
            if (doll.TryGetComponent(out Rigidbody rigidbody))
            {
                rigidbody.isKinematic = true;
            }
            if (doll.TryGetComponent(out ProjectileController controller))
            {
                controller.owner = body.gameObject;
            }
            if (doll.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = body.teamComponent.teamIndex;
            }
            if (doll.TryGetComponent(out TeamComponent teamComponent))
            {
                teamComponent.teamIndex = body.teamComponent.teamIndex;
            }
            var walker = doll.AddComponent<WalkingDoll>();
            walker.owner = body;
            walker.direction = direction;
            NetworkServer.Spawn(doll);
            return true;
        }

        private bool Inflate(EquipmentSlot slot)
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

        private static bool FireShrimpMissiles(CharacterBody body)
        {
            var fired = false;
            foreach (var target in CharacterBody.readOnlyInstancesList)
            {
                if (target == null || target.mainHurtBox == null || target.healthComponent == null || !target.healthComponent.alive
                    || target.teamComponent.teamIndex == body.teamComponent.teamIndex || target.teamComponent.teamIndex == TeamIndex.Neutral)
                {
                    continue;
                }
                var orb = new MissileVoidOrb
                {
                    origin = body.aimOrigin,
                    damageValue = body.damage * ShrimpMissileDamage,
                    isCrit = body.RollCrit(),
                    teamIndex = body.teamComponent.teamIndex,
                    attacker = body.gameObject,
                    procCoefficient = 0.5f,
                    damageColorIndex = DamageColorIndex.Void,
                    target = target.mainHurtBox
                };
                orb.procChainMask.AddProc(ProcType.Missile);
                OrbManager.instance.AddOrb(orb);
                fired = true;
            }
            return fired;
        }

        private void GlobalEventManager_OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            var attackerBody = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (NetworkServer.active && IsActive() && equipmentDef == RoR2Content.Equipment.LifestealOnHit && attackerBody != null
                && attackerBody.HasBuff(DLC3Content.Buffs.ShieldTankDamage) && attackerBody.GetComponent<RemixComponent>() != null
                && damageInfo.procCoefficient > 0f && !damageInfo.rejected && victim != null)
            {
                DotController.InflictDot(victim, damageInfo.attacker, damageInfo.inflictedHurtbox, damageInfo.procChainMask, DotController.DotIndex.SuperBleed, 15f * damageInfo.procCoefficient);
            }
        }

        public override void Prepare()
        {
            equipmentDef = Equipments[RoR2Application.rng.RangeInt(0, Equipments.Length)];
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            if (equipmentDef == null)
            {
                Prepare();
            }
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                Give(body);
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.TryGetComponent(out RemixComponent remix))
                {
                    UnityEngine.Object.Destroy(remix);
                }
            }
            equipmentDef = null;
        }

        public class RemixComponent : MonoBehaviour
        {
            private TemporaryEquipment equipment;

            public void Setup(EquipmentDef equipmentDef)
            {
                equipment = gameObject.AddComponent<TemporaryEquipment>();
                // a quarter for the missiles, the leech and the scanner, half for the rest
                equipment.cooldownScale = equipmentDef == RoR2Content.Equipment.CommandMissile || equipmentDef == RoR2Content.Equipment.LifestealOnHit
                    || equipmentDef == RoR2Content.Equipment.Scanner ? 0.25f : 0.5f;
                equipment.SetTemporaryEquipment(equipmentDef.equipmentIndex);
            }

            private void OnDisable()
            {
                if (equipment != null)
                {
                    Destroy(equipment);
                }
            }
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

            private void Start()
            {
                body = GetComponent<CharacterBody>();
                startPercent = 100 + body.inventory.GetItemCountEffective(DLC4Content.Items.BoostCharacterScale) - body.inventory.GetItemCountEffective(DLC4Content.Items.ReduceCharacterScale);
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || body == null)
                {
                    return;
                }
                time += Time.fixedDeltaTime;
                var progress = Mathf.Clamp01(time / InflateTime);
                var target = Mathf.RoundToInt(startPercent * (InflateFactor - 1f) * progress * progress);
                if (target > added && body.inventory != null)
                {
                    body.inventory.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, target - added);
                    added = target;
                }
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
                if (progress >= 1f || !body.healthComponent.alive)
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
                    body.healthComponent.Suicide(attacker != null ? attacker.gameObject : null);
                }
                Destroy(this);
            }
        }

        // Walks along the ground, bouncing off walls, then goes off like Hot Potato's bomb, sparing players
        public class WalkingDoll : MonoBehaviour
        {
            public CharacterBody owner;
            public Vector3 direction;
            private const float StepRate = 12f;
            private float age;

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                var position = transform.position;
                var step = direction * DollSpeed * Time.fixedDeltaTime;
                // a ramp it walks up, a wall it bounces off
                if (Physics.Raycast(position, direction, out var wall, step.magnitude + 0.5f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && wall.normal.y < 0.5f)
                {
                    // reflected along the ground; straight back if that leaves no direction
                    var reflected = Vector3.Reflect(direction, wall.normal);
                    reflected.y = 0f;
                    direction = reflected.sqrMagnitude > 0.01f ? reflected.normalized : -direction;
                }
                else
                {
                    position += step;
                }
                // on the ground below, or falling if there is none
                // with a little hop for each step, highest between leaning to one side and the other
                var sway = Mathf.Sin(age * StepRate);
                if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out var hit, 6f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    position.y = hit.point.y + 0.5f + (1f - Mathf.Abs(sway)) * 0.25f;
                }
                else
                {
                    position.y -= 10f * Time.fixedDeltaTime;
                }
                // Upright: the doll's model lies along its forward axis, head first and face down, so that axis points up
                // and its down axis ahead. Then a waddle from side to side, and a nod of the head with every step.
                var nod = Mathf.Sin(age * StepRate * 2f) * 10f;
                var upright = Quaternion.LookRotation(Vector3.up, -direction);
                var side = Vector3.Cross(Vector3.up, direction);
                transform.SetPositionAndRotation(position, Quaternion.AngleAxis(sway * 15f, direction) * Quaternion.AngleAxis(nod, side) * upright);
                if (age >= DollFuse)
                {
                    var groundZero = Physics.Raycast(position, Vector3.down, out var floor, 50f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? floor.point : position;
                    EventHotPotato.Nuke(groundZero, DollNukeRadius, 0f, 2f, 0.5f, owner != null ? owner.gameObject : null);
                    Destroy(gameObject);
                }
            }
        }

        // Radar Scanner's wave, travelling outwards as fast as its pulse, pushes away and hurts each monster it reaches
        public class ScannerWave : MonoBehaviour
        {
            public CharacterBody attacker;
            private Vector3 origin;
            private float radius;
            private readonly HashSet<CharacterBody> hit = new HashSet<CharacterBody>();

            private void Start()
            {
                origin = attacker != null ? attacker.corePosition : transform.position;
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
                        force = (direction * 30f + Vector3.up * 15f) * Mathf.Min(mass, 1000f),
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
