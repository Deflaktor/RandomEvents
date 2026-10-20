using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class RemixCrowdfunder : RemixedEquipment
    {
        // the Crowdfunder fires a Resonance Disc's shot with every so many of its bullets, starting with the first
        private const int BulletsPerDiscShot = 10;
        private const float DiscBeamDamage = 3f;
        private const float DiscBombDamage = 10f;
        private static AsyncOperationHandle<GameObject> DiscTracer;
        private static AsyncOperationHandle<GameObject> DiscBomb;
        // each Crowdfunder's bullets so far
        private readonly Dictionary<GameObject, int> crowdfunderBullets = new Dictionary<GameObject, int>();
        // the players the Crowdfunder roots while it fires
        private readonly BuffTracker roots = new BuffTracker();

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.GoldGat;
        public override string Description => "the Crowdfunder costs no gold and fires Resonance Disc shots, but roots you while it fires";
        public override string PopupDescription => "Costs no gold and fires Resonance Disc shots, but you can't move while it fires.";

        public override void Preload()
        {
            DiscTracer = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LaserTurbine/TracerLaserTurbine.prefab");
            DiscBomb = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LaserTurbine/LaserTurbineBomb.prefab");
        }

        public override void Hook()
        {
            On.EntityStates.GoldGat.GoldGatFire.FireBullet += GoldGatFire_FireBullet;
            On.EntityStates.GoldGat.GoldGatFire.OnEnter += GoldGatFire_OnEnter;
            On.EntityStates.GoldGat.GoldGatFire.OnExit += GoldGatFire_OnExit;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // it fires as usual, toggled by the player's own game
        public override bool ReplacesAction => false;

        // it only starts firing with gold
        public override void FixedUpdatePlayer(CharacterBody body)
        {
            if (body.master != null && body.master.money == 0)
            {
                body.master.money = 1;
            }
        }

        public override void Stop()
        {
            roots.RemoveAll();
            crowdfunderBullets.Clear();
        }

        // The Crowdfunder's state runs on every machine, the bullets only on the shooting player's. On the host it takes
        // the gold per bullet, which goes back, and fires the Resonance Disc's shots.
        private void GoldGatFire_FireBullet(On.EntityStates.GoldGat.GoldGatFire.orig_FireBullet orig, EntityStates.GoldGat.GoldGatFire self)
        {
            var body = self.body;
            if (!IsRemixed(body) || self.bodyMaster == null)
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
            if (IsRemixed(self.body))
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
            if (damageInfo != null && damageInfo.inflictor != null && IsActive()
                && damageInfo.inflictor.TryGetComponent(out EntityStateMachine stateMachine) && stateMachine.state is EntityStates.GoldGat.BaseGoldGatState)
            {
                damageInfo.rejected = true;
                return;
            }
            orig(self, damageInfo);
        }
    }
}
