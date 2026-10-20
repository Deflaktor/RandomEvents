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
        // the Crowdfunder fires a Resonance Disc's shot with every so many of its bullets, starting with the first
        private const int BulletsPerDiscShot = 10;
        private const float DiscBeamDamage = 3f;
        private const float DiscBombDamage = 10f;
        private static AsyncOperationHandle<GameObject> DiscTracer;
        private static AsyncOperationHandle<GameObject> DiscBomb;
        private static AsyncOperationHandle<GameObject> MolotovBottle;
        private static AsyncOperationHandle<GameObject> Sawmerang;
        // each Crowdfunder's bullets so far
        private readonly Dictionary<GameObject, int> crowdfunderBullets = new Dictionary<GameObject, int>();
        // the players the Crowdfunder roots while it fires
        private readonly BuffTracker roots = new BuffTracker();
        private static AsyncOperationHandle<GameObject> Breach;
        private static AsyncOperationHandle<GameObject> Tick;
        private const float LeechDuration = 8f;
        private EquipmentDef equipmentDef;

        private static EquipmentDef[] Equipments => new[]
        {
            RoR2Content.Equipment.Lightning, RoR2Content.Equipment.CommandMissile, RoR2Content.Equipment.Scanner, RoR2Content.Equipment.LifestealOnHit,
            RoR2Content.Equipment.DeathProjectile, RoR2Content.Equipment.GoldGat, DLC1Content.Equipment.Molotov, RoR2Content.Equipment.Saw
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
            return "Gives all players a remixed equipment with a shorter cooldown. Royal Capacitor inflates a monster until it explodes, Disposable Missile Launcher fires a Plasma Shrimp missile at every monster, Radar Scanner's wave also pushes and hurts monsters, and Super Massive Leech causes hemorrhage instead of healing, Forgive Me Please's doll walks straight ahead and explodes like a mini nuke after 10 seconds, the Crowdfunder costs no gold and fires Resonance Disc shots, but roots you while it fires, the Molotov (6-Pack) sets a sea of fire under a blazing sun that burns everything nearby harder and harder for 5 seconds, and the Sawmerang's blades hunt monsters, cutting through them back and forth for 15 seconds.";
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
            if (equipmentDef == RoR2Content.Equipment.GoldGat)
                return "Costs no gold and fires Resonance Disc shots, but you can't move while it fires.";
            if (equipmentDef == DLC1Content.Equipment.Molotov)
                return "One bottle that sets a sea of fire under a blazing sun, burning everything nearby harder and harder for 5 seconds.";
            if (equipmentDef == RoR2Content.Equipment.Saw)
                return "The blades hunt the nearest monster, cutting through it back and forth, then the next, for 15 seconds.";
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
            DiscTracer = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LaserTurbine/TracerLaserTurbine.prefab");
            DiscBomb = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LaserTurbine/LaserTurbineBomb.prefab");
            MolotovBottle = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovSingleProjectile.prefab");
            Sawmerang = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Saw/Sawmerang.prefab");
            Inferno.Preload();
            EventHotPotato.MiniNuke.Preload();
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.EquipmentSlot.PerformEquipmentAction += EquipmentSlot_PerformEquipmentAction;
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
            On.EntityStates.GoldGat.GoldGatFire.FireBullet += GoldGatFire_FireBullet;
            On.EntityStates.GoldGat.GoldGatFire.OnEnter += GoldGatFire_OnEnter;
            On.EntityStates.GoldGat.GoldGatFire.OnExit += GoldGatFire_OnExit;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.Projectile.ProjectileExplosion.DetonateServer += ProjectileExplosion_DetonateServer;
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
            if (def == RoR2Content.Equipment.Saw)
            {
                LaunchSaws(self);
                return true;
            }
            if (def == DLC1Content.Equipment.Molotov)
            {
                var aim = self.GetAimRay();
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = MolotovBottle.WaitForCompletion(),
                    owner = self.characterBody.gameObject,
                    position = aim.origin,
                    rotation = Quaternion.LookRotation(aim.direction),
                    damage = self.characterBody.damage,
                    crit = self.characterBody.RollCrit(),
                    useSpeedOverride = true,
                    speedOverride = 40f
                });
                return true;
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

        // the remixed Molotov's bottle sets its sea of fire instead of the bottle's own fire
        private void ProjectileExplosion_DetonateServer(On.RoR2.Projectile.ProjectileExplosion.orig_DetonateServer orig, ProjectileExplosion self)
        {
            var owner = self.projectileController != null && self.projectileController.owner != null ? self.projectileController.owner.GetComponent<CharacterBody>() : null;
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || equipmentDef != DLC1Content.Equipment.Molotov || owner == null
                || owner.GetComponent<RemixComponent>() == null || !self.name.StartsWith("MolotovSingleProjectile"))
            {
                orig(self);
                return;
            }
            var center = Physics.Raycast(self.transform.position + Vector3.up, Vector3.down, out var hit, 30f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : self.transform.position;
            var inferno = new GameObject("RandomEventsInferno").AddComponent<Inferno>();
            inferno.owner = owner;
            inferno.center = center;
        }

        // three blades, fanned out like the vanilla ones, which then hunt on their own
        private static void LaunchSaws(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            var aim = slot.GetAimRay();
            foreach (var angle in new[] { -15f, 0f, 15f })
            {
                var rotation = Quaternion.LookRotation(aim.direction) * Quaternion.Euler(0f, angle, 0f);
                var saw = UnityEngine.Object.Instantiate(Sawmerang.WaitForCompletion(), aim.origin, rotation);
                if (saw.TryGetComponent(out BoomerangProjectile boomerang))
                {
                    boomerang.enabled = false;
                }
                if (saw.TryGetComponent(out Rigidbody rigidbody))
                {
                    rigidbody.isKinematic = true;
                }
                if (saw.TryGetComponent(out ProjectileController controller))
                {
                    controller.owner = body.gameObject;
                }
                if (saw.TryGetComponent(out TeamFilter teamFilter))
                {
                    teamFilter.teamIndex = body.teamComponent.teamIndex;
                }
                if (saw.TryGetComponent(out ProjectileDamage projectileDamage))
                {
                    projectileDamage.damage = body.damage;
                    projectileDamage.crit = body.RollCrit();
                    projectileDamage.damageColorIndex = DamageColorIndex.Item;
                }
                var hunter = saw.AddComponent<HuntingSaw>();
                hunter.owner = body;
                hunter.direction = rotation * Vector3.forward;
                NetworkServer.Spawn(saw);
            }
        }

        private bool IsRemixedCrowdfunder(CharacterBody body)
        {
            return ModConfig.Enabled.Value && NetworkServer.active && IsActive() && equipmentDef == RoR2Content.Equipment.GoldGat && body != null
                && body.GetComponent<RemixComponent>() != null;
        }

        // The Crowdfunder's state runs on every machine, the bullets only on the shooting player's. On the host it takes
        // the gold per bullet, which goes back, and fires the Resonance Disc's shots.
        private void GoldGatFire_FireBullet(On.EntityStates.GoldGat.GoldGatFire.orig_FireBullet orig, EntityStates.GoldGat.GoldGatFire self)
        {
            var body = self.body;
            if (!IsRemixedCrowdfunder(body) || self.bodyMaster == null)
            {
                orig(self);
                return;
            }
            var money = self.bodyMaster.money;
            orig(self);
            self.bodyMaster.money = money;
            var bullets = crowdfunderBullets.GetValueOrDefault(self.gameObject);
            crowdfunderBullets[self.gameObject] = bullets + 1;
            if (bullets % BulletsPerDiscShot == 0 && body.inputBank != null)
            {
                FireDiscShot(body, new Ray(body.inputBank.aimOrigin, body.inputBank.aimDirection));
            }
        }

        // the Resonance Disc's laser, and its bomb where the laser hits
        private static void FireDiscShot(CharacterBody body, Ray aim)
        {
            var crit = body.RollCrit();
            new BulletAttack
            {
                owner = body.gameObject,
                weapon = body.gameObject,
                origin = aim.origin,
                aimVector = aim.direction,
                damage = body.damage * DiscBeamDamage,
                damageColorIndex = DamageColorIndex.Item,
                isCrit = crit,
                force = 1000f,
                procCoefficient = 1f,
                radius = 1f,
                maxDistance = 1000f,
                smartCollision = true,
                stopperMask = LayerIndex.world.mask,
                tracerEffectPrefab = DiscTracer.WaitForCompletion()
            }.Fire();
            var hitPoint = Physics.Raycast(aim, out var hit, 1000f, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore) ? hit.point : aim.GetPoint(1000f);
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = DiscBomb.WaitForCompletion(),
                owner = body.gameObject,
                position = hitPoint - aim.direction * 0.5f,
                rotation = Quaternion.identity,
                damage = body.damage * DiscBombDamage,
                damageColorIndex = DamageColorIndex.Item,
                crit = crit
            });
        }

        // rooted while it fires, with the vines of the root from the Hooks of Heresy
        private void GoldGatFire_OnEnter(On.EntityStates.GoldGat.GoldGatFire.orig_OnEnter orig, EntityStates.GoldGat.GoldGatFire self)
        {
            if (IsRemixedCrowdfunder(self.body))
            {
                crowdfunderBullets[self.gameObject] = 0;
                if (!roots.Has(self.body, RoR2Content.Buffs.LunarSecondaryRoot))
                {
                    roots.Add(self.body, RoR2Content.Buffs.LunarSecondaryRoot);
                }
            }
            orig(self);
        }

        private void GoldGatFire_OnExit(On.EntityStates.GoldGat.GoldGatFire.orig_OnExit orig, EntityStates.GoldGat.GoldGatFire self)
        {
            if (NetworkServer.active && self.body != null)
            {
                roots.Remove(self.body, RoR2Content.Buffs.LunarSecondaryRoot);
            }
            orig(self);
        }

        // the Crowdfunder's own bullets do nothing, as the disc's shots replace them; they show for the shooting player
        // all the same, whose own game fires them
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (damageInfo != null && damageInfo.inflictor != null && NetworkServer.active && IsActive() && equipmentDef == RoR2Content.Equipment.GoldGat
                && damageInfo.inflictor.TryGetComponent(out EntityStateMachine stateMachine) && stateMachine.state is EntityStates.GoldGat.BaseGoldGatState)
            {
                damageInfo.rejected = true;
                return;
            }
            orig(self, damageInfo);
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
            // only those of the run's expansions, e.g. the Molotov (6-Pack) with Survivors of the Void
            var available = Equipments.Where(def => def != null && (def.requiredExpansion == null || Run.instance == null || Run.instance.IsExpansionEnabled(def.requiredExpansion))).ToList();
            equipmentDef = available[RoR2Application.rng.RangeInt(0, available.Count)];
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
            roots.RemoveAll();
            crowdfunderBullets.Clear();
            equipmentDef = null;
        }

        public class RemixComponent : MonoBehaviour
        {
            private TemporaryEquipment equipment;
            private bool crowdfunder;

            public void Setup(EquipmentDef equipmentDef)
            {
                crowdfunder = equipmentDef == RoR2Content.Equipment.GoldGat;
                equipment = gameObject.AddComponent<TemporaryEquipment>();
                // a quarter for the missiles, the leech and the scanner, half for the rest
                equipment.cooldownScale = equipmentDef == RoR2Content.Equipment.CommandMissile || equipmentDef == RoR2Content.Equipment.LifestealOnHit
                    || equipmentDef == RoR2Content.Equipment.Scanner ? 0.25f : 0.5f;
                equipment.SetTemporaryEquipment(equipmentDef.equipmentIndex);
            }

            // the Crowdfunder only starts firing with gold
            private void FixedUpdate()
            {
                var master = crowdfunder && NetworkServer.active && TryGetComponent(out CharacterBody body) ? body.master : null;
                if (master != null && master.money == 0)
                {
                    master.money = 1;
                }
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
                    body.healthComponent.Suicide(attacker != null ? attacker.gameObject : null);
                }
                Destroy(this);
            }
        }

        // The remixed Molotov's sea of fire: a Grandparent's sun blazes where it landed, and for 5 seconds everything
        // hostile nearby burns, with stacks that pile up faster and faster to 10000% of the thrower's damage in all,
        // which the Grandparent's heat shows. Its middle burns continuously as long as the fire lasts, 10 seconds, with
        // the ground covered in Molotov fire, CHEF's burning oil and the Spirit's rings of fire, all just for show.
        public class Inferno : MonoBehaviour
        {
            private const float Duration = 10f;
            private const float BurnDuration = 5f;
            private const float SunHeight = 4f;
            private const float Radius = 12f;
            private const float TickInterval = 0.1f;
            private const float TotalDamage = 100f;
            private const int MaxHeatStacks = 20;
            // few of the parts with their own sound, which add up quickly
            private const float RingInterval = 3f;
            private const float FireballInterval = 2f;
            private const int OilPuddles = 2;
            // the center burns on its own too, for as long as the fire lasts
            private const float CoreRadius = 5f;
            private const float CoreDamagePerSecond = 3f;
            private const float CoreTickInterval = 0.25f;
            private static AsyncOperationHandle<GameObject> FirePool;
            private static AsyncOperationHandle<GameObject> BurningOil;
            private static AsyncOperationHandle<GameObject> RingOfFire;
            private static AsyncOperationHandle<GameObject> Ignition;
            private static AsyncOperationHandle<GameObject> Fireball;
            private static AsyncOperationHandle<GameObject> Sun;
            public CharacterBody owner;
            public Vector3 center;
            private readonly List<GameObject> fires = new List<GameObject>();
            private readonly Dictionary<CharacterBody, int> heat = new Dictionary<CharacterBody, int>();
            private float age;
            private int tick;
            private float ringTimer;
            private float fireballTimer;
            private float coreTimer;
            private int ticks;
            private float weightSum;
            private GenericOwnership sun;

            public static void Preload()
            {
                FirePool = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovProjectileDotZone.prefab");
                BurningOil = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Chef/ChefImpactOilBoosted.prefab");
                RingOfFire = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/Spirit/RingOfFire.prefab");
                Ignition = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovClusterIgniteExplosionVFX.prefab");
                Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Chef/BoostedSearFireballProjectileExplosionVFX.prefab");
                Sun = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Grandparent/GrandParentSun.prefab");
            }

            private void Start()
            {
                ticks = Mathf.RoundToInt(BurnDuration / TickInterval);
                for (int i = 1; i <= ticks; i++)
                {
                    weightSum += i * i;
                }
                EffectManager.SpawnEffect(Ignition.WaitForCompletion(), new EffectData { origin = center, scale = Radius }, transmit: true);
                EffectManager.SpawnEffect(Fireball.WaitForCompletion(), new EffectData { origin = center, scale = Radius * 0.5f }, transmit: true);
                SpawnSun();
                SpawnFire(FirePool, center);
                for (int i = 0; i < 10; i++)
                {
                    SpawnFire(FirePool, OnGround(center + RandomHorizontal(Radius * 0.85f)));
                }
                for (int i = 0; i < OilPuddles; i++)
                {
                    SpawnFire(BurningOil, OnGround(center + RandomHorizontal(Radius * 0.7f)));
                }
            }

            private static Vector3 RandomHorizontal(float radius)
            {
                var circle = UnityEngine.Random.insideUnitCircle * radius;
                return new Vector3(circle.x, 0f, circle.y);
            }

            // Its burning runs on the host alone, and is off; it shines while it has an owner, then fades out by itself
            private void SpawnSun()
            {
                if (owner == null)
                {
                    return;
                }
                var sunObject = Instantiate(Sun.WaitForCompletion(), center + Vector3.up * SunHeight, Quaternion.identity);
                if (sunObject.TryGetComponent(out GrandParentSunController controller))
                {
                    controller.enabled = false;
                }
                if (sunObject.TryGetComponent(out TeamFilter teamFilter))
                {
                    teamFilter.teamIndex = owner.teamComponent.teamIndex;
                }
                sun = sunObject.GetComponent<GenericOwnership>();
                if (sun != null)
                {
                    sun.ownerObject = owner.gameObject;
                }
                NetworkServer.Spawn(sunObject);
            }

            private void PutOutSun()
            {
                if (sun != null)
                {
                    sun.ownerObject = null;
                }
                sun = null;
            }

            private static Vector3 OnGround(Vector3 position)
            {
                return Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out var hit, 15f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : position;
            }

            // A projectile for its look only: every part that would hurt or spread runs on the host alone, and is off
            private GameObject SpawnFire(AsyncOperationHandle<GameObject> prefab, Vector3 position)
            {
                var fire = Instantiate(prefab.WaitForCompletion(), position, Quaternion.identity);
                if (fire.TryGetComponent(out OilController oil))
                {
                    // it sets itself on fire as it starts, which turns its damage zone on again
                    oil.enabled = false;
                }
                foreach (var behaviour in new Behaviour[] { fire.GetComponent<ProjectileDotZone>(), fire.GetComponent<ProjectileOverlapAttack>(), fire.GetComponent<SpiritRingOfFireProjectile>() })
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                    }
                }
                if (fire.TryGetComponent(out ProjectileDamage projectileDamage))
                {
                    projectileDamage.damage = 0f;
                }
                if (fire.TryGetComponent(out ProjectileController controller) && owner != null)
                {
                    controller.owner = owner.gameObject;
                }
                if (fire.TryGetComponent(out TeamFilter teamFilter) && owner != null)
                {
                    teamFilter.teamIndex = owner.teamComponent.teamIndex;
                }
                NetworkServer.Spawn(fire);
                fires.Add(fire);
                return fire;
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                ringTimer -= Time.fixedDeltaTime;
                if (ringTimer <= 0f && age < Duration)
                {
                    ringTimer = RingInterval;
                    SpawnFire(RingOfFire, center);
                }
                fireballTimer -= Time.fixedDeltaTime;
                if (fireballTimer <= 0f && age < Duration)
                {
                    fireballTimer = FireballInterval;
                    EffectManager.SpawnEffect(Fireball.WaitForCompletion(), new EffectData { origin = OnGround(center + RandomHorizontal(Radius)), scale = 4f }, transmit: true);
                }
                coreTimer -= Time.fixedDeltaTime;
                if (coreTimer <= 0f && age < Duration)
                {
                    coreTimer = CoreTickInterval;
                    BurnCore();
                }
                while (tick < ticks && age >= (tick + 1) * TickInterval)
                {
                    tick++;
                    Burn(tick);
                }
                if (age >= BurnDuration && sun != null)
                {
                    PutOutSun();
                    RemoveHeat();
                }
                if (age >= Duration)
                {
                    End();
                }
            }

            // the share of the burn for this tick grows with its square, so the stacks pile up faster and faster
            private void Burn(int tickNumber)
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return;
                }
                var damage = TotalDamage * owner.damage * tickNumber * tickNumber / weightSum;
                var heatStacks = Mathf.Min(MaxHeatStacks, Mathf.CeilToInt(MaxHeatStacks * (float)tickNumber / ticks));
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive || body.teamComponent.teamIndex == owner.teamComponent.teamIndex
                        || body.teamComponent.teamIndex == TeamIndex.Neutral || (body.corePosition - center).sqrMagnitude > (Radius + body.radius) * (Radius + body.radius))
                    {
                        continue;
                    }
                    var info = new InflictDotInfo
                    {
                        attackerObject = owner.gameObject,
                        victimObject = body.gameObject,
                        dotIndex = DotController.DotIndex.Burn,
                        damageMultiplier = 1f,
                        totalDamage = damage,
                        hitHurtBox = body.mainHurtBox
                    };
                    DotController.InflictDot(ref info);
                    var added = heat.GetValueOrDefault(body);
                    if (heatStacks > added)
                    {
                        body.SetBuffCount(RoR2Content.Buffs.Overheat.buffIndex, body.GetBuffCount(RoR2Content.Buffs.Overheat) + heatStacks - added);
                        heat[body] = heatStacks;
                    }
                }
            }

            // 300% of the thrower's damage per second to everything hostile in the middle
            private void BurnCore()
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return;
                }
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive || body.teamComponent.teamIndex == owner.teamComponent.teamIndex
                        || body.teamComponent.teamIndex == TeamIndex.Neutral || (body.corePosition - center).sqrMagnitude > (CoreRadius + body.radius) * (CoreRadius + body.radius))
                    {
                        continue;
                    }
                    body.healthComponent.TakeDamage(new DamageInfo
                    {
                        attacker = owner.gameObject,
                        inflictor = gameObject,
                        damage = owner.damage * CoreDamagePerSecond * CoreTickInterval,
                        crit = owner.RollCrit(),
                        position = body.corePosition,
                        procCoefficient = 0f,
                        damageColorIndex = DamageColorIndex.Item
                    });
                }
            }

            private void RemoveHeat()
            {
                foreach (var (body, added) in heat)
                {
                    if (body != null)
                    {
                        body.SetBuffCount(RoR2Content.Buffs.Overheat.buffIndex, Mathf.Max(0, body.GetBuffCount(RoR2Content.Buffs.Overheat) - added));
                    }
                }
                heat.Clear();
            }

            private void End()
            {
                PutOutSun();
                RemoveHeat();
                foreach (var fire in fires)
                {
                    if (fire != null)
                    {
                        Destroy(fire);
                    }
                }
                fires.Clear();
                Destroy(gameObject);
            }
        }

        // The blade flies at the nearest monster and through it, a little past it, then turns back for another pass, so
        // it cuts it again and again; once the monster dies, the next nearest. The host moves it, which its projectile
        // transform passes on to everyone.
        public class HuntingSaw : MonoBehaviour
        {
            private const float Lifetime = 15f;
            private const float Speed = 40f;
            private const float TurnRate = 720f;
            private const float Overshoot = 0.25f;
            private const float SearchRadius = 120f;
            public CharacterBody owner;
            public Vector3 direction;
            private CharacterBody target;
            private ProjectileOverlapAttack overlapAttack;
            private float age;
            // how long it keeps going straight after passing through its target
            private float passTimer;

            private void Start()
            {
                overlapAttack = GetComponent<ProjectileOverlapAttack>();
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                if (age >= Lifetime)
                {
                    Destroy(gameObject);
                    return;
                }
                if (target == null || target.healthComponent == null || !target.healthComponent.alive)
                {
                    target = FindTarget();
                    passTimer = 0f;
                }
                var position = transform.position;
                if (target != null)
                {
                    var toTarget = target.corePosition - position;
                    if (passTimer > 0f)
                    {
                        passTimer -= Time.fixedDeltaTime;
                        if (passTimer <= 0f && overlapAttack != null)
                        {
                            // a new pass hits it again
                            overlapAttack.ResetOverlapAttack();
                        }
                    }
                    else if (toTarget.sqrMagnitude <= target.radius * target.radius + 1f || Vector3.Dot(toTarget, direction) < 0f && toTarget.sqrMagnitude < 25f)
                    {
                        passTimer = Overshoot;
                    }
                    else
                    {
                        direction = Vector3.RotateTowards(direction, toTarget.normalized, TurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                    }
                }
                else if (owner != null)
                {
                    // nothing to hunt: circle back to its owner
                    var toOwner = owner.corePosition + Vector3.up * 3f - position;
                    if (toOwner.sqrMagnitude > 9f)
                    {
                        direction = Vector3.RotateTowards(direction, toOwner.normalized, TurnRate * 0.5f * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                    }
                }
                transform.SetPositionAndRotation(position + direction * Speed * Time.fixedDeltaTime, Util.QuaternionSafeLookRotation(direction));
            }

            private CharacterBody FindTarget()
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return null;
                }
                var position = transform.position;
                return CharacterBody.readOnlyInstancesList
                    .Where(body => body != null && body.healthComponent != null && body.healthComponent.alive && body.teamComponent.teamIndex != owner.teamComponent.teamIndex
                        && body.teamComponent.teamIndex != TeamIndex.Neutral && (body.corePosition - position).sqrMagnitude <= SearchRadius * SearchRadius)
                    .OrderBy(body => (body.corePosition - position).sqrMagnitude)
                    .FirstOrDefault();
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
