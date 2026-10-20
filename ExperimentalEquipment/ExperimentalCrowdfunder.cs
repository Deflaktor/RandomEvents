using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // The Crowdfunder fires as usual, but costs no gold, its hits push monsters back, every 3 of its hits launch a
    // monster like the Breaching Fin, even one in the air, and every 4 make it heavier. A monster made heavier dies when it
    // falls out of the map, even one that would be brought back.
    public class ExperimentalCrowdfunder : ExperimentalEquipment
    {
        private const int HitsPerLaunch = 3;
        private const int HitsPerGravity = 4;
        // each hit pushes it this fast away from the shooter, for its weight, a little up too
        private const float KnockbackSpeed = 2.5f;
        // how much the gravity grows each time, up to the most
        private const float GravityStep = 1f;
        private const float MaxGravityScale = 6f;
        private readonly Dictionary<CharacterBody, int> hits = new Dictionary<CharacterBody, int>();
        private readonly HashSet<CharacterBody> heavier = new HashSet<CharacterBody>();

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.GoldGat;
        public override string Description => "the Crowdfunder costs no gold, its hits push monsters back, every 3 of its hits launch a monster like the Breaching Fin, again and again, and every 4 make it heavier, so it dies if it falls out of the map";
        public override string PopupDescription => "Costs no gold and its hits push monsters back. Every 3 hits launch a monster like the Breaching Fin, again and again, and every 4 make it heavier. A heavier monster that falls out of the map dies.";

        // it fires as usual, toggled by the player's own game
        public override bool ReplacesAction => false;

        public override void Hook()
        {
            On.EntityStates.GoldGat.GoldGatFire.FireBullet += GoldGatFire_FireBullet;
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
            On.RoR2.MapZone.CharacterEligibleForTeleport += MapZone_CharacterEligibleForTeleport;
        }

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
            hits.Clear();
            heavier.Clear();
        }

        // the host takes the gold for each bullet, which goes back
        private void GoldGatFire_FireBullet(On.EntityStates.GoldGat.GoldGatFire.orig_FireBullet orig, EntityStates.GoldGat.GoldGatFire self)
        {
            if (!IsExperimental(self.body) || self.bodyMaster == null)
            {
                orig(self);
                return;
            }
            var money = self.bodyMaster.money;
            orig(self);
            self.bodyMaster.money = money;
        }

        // The bullets come from the gun, also those a player's own game fires, whose hits the host gets
        private void GlobalEventManager_OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            var attackerBody = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (damageInfo.rejected || victim == null || damageInfo.inflictor == null || !IsExperimental(attackerBody)
                || !damageInfo.inflictor.TryGetComponent(out EntityStateMachine stateMachine) || !(stateMachine.state is EntityStates.GoldGat.BaseGoldGatState)
                || !victim.TryGetComponent(out CharacterBody body) || body.teamComponent.teamIndex == TeamIndex.Player)
            {
                return;
            }
            var away = body.corePosition - attackerBody.corePosition;
            var mass = body.characterMotor != null ? body.characterMotor.mass : body.rigidbody != null ? body.rigidbody.mass : 100f;
            body.healthComponent.TakeDamageForce((new Vector3(away.x, 0f, away.z).normalized + Vector3.up * 0.3f) * KnockbackSpeed * Mathf.Min(mass, 1000f), alwaysApply: true);
            var count = hits.GetValueOrDefault(body) + 1;
            hits[body] = count;
            if (count % HitsPerLaunch == 0)
            {
                Launch(attackerBody, body);
            }
            if (count % HitsPerGravity == 0)
            {
                MakeHeavier(body);
            }
        }

        // its AI runs on the host, so the host's motor is the one that moves it; one that flies starts to fall
        private void MakeHeavier(CharacterBody body)
        {
            if (body.characterMotor != null)
            {
                body.characterMotor.gravityScale = Mathf.Min(MaxGravityScale, body.characterMotor.gravityScale + GravityStep);
            }
            else if (body.rigidbody != null)
            {
                body.rigidbody.useGravity = true;
            }
            heavier.Add(body);
        }

        // The Breaching Fin's launch, as the game does it, but also in the air, with no limit and no cooldown: up and a
        // little away from the shooter, stunned, and each launch before it lands adds 10% to the damage it takes
        private static void Launch(CharacterBody attacker, CharacterBody body)
        {
            var motor = body.characterMotor;
            if (motor == null || (body.bodyFlags & (CharacterBody.BodyFlags.IgnoreKnockup | CharacterBody.BodyFlags.Unmovable)) != 0
                || body.HasBuff(RoR2Content.Buffs.Immune))
            {
                return;
            }
            // launched: the launches it took stay until it has been back on the ground for a moment
            body.SetBuffCount(DLC2Content.Buffs.KnockUpHitEnemies.buffIndex, Mathf.Max(1, body.GetBuffCount(DLC2Content.Buffs.KnockUpHitEnemies)));
            body.AddBuff(DLC2Content.Buffs.KnockUpHitEnemiesJuggleCount);
            if (body.TryGetComponent(out SetStateOnHurt setStateOnHurt))
            {
                setStateOnHurt.OverrideStun(KnockbackFinUtil.StunDuration);
            }
            var scale = body.hullClassification switch
            {
                HullClassification.Golem => KnockbackFinUtil.golemLaunchEffectScale,
                HullClassification.BeetleQueen => KnockbackFinUtil.queenLaunchEffectScale,
                _ => KnockbackFinUtil.humanLaunchEffectScale
            };
            EffectManager.SpawnEffect(CharacterBody.CommonAssets.knockbackFinEffect, new EffectData
            {
                origin = body.transform.position,
                scale = scale
            }, transmit: true);
            var impulse = motor.mass * KnockbackFinUtil.victimMassMultiplier;
            if (body.isBoss)
            {
                impulse *= KnockbackFinUtil.bossImpulseModifier;
            }
            else if (body.isChampion)
            {
                impulse *= KnockbackFinUtil.championImpulseModifier;
            }
            var modifier = motor.isGrounded ? 1f : KnockbackFinUtil.juggleModifier01;
            // a falling one is caught first
            var up = impulse * modifier - Mathf.Min(0f, motor.velocity.y) * motor.mass;
            var away = body.corePosition - attacker.corePosition;
            away.y = 0f;
            motor.ApplyForce(up * Vector3.up + impulse * KnockbackFinUtil.knockBackardsRatio * modifier * away.normalized, alwaysApply: true);
        }

        // out of the map, it dies instead of being brought back; its last attacker gets the kill
        private bool MapZone_CharacterEligibleForTeleport(On.RoR2.MapZone.orig_CharacterEligibleForTeleport orig, MapZone self, CharacterBody characterBody)
        {
            if (IsActive() && characterBody != null && heavier.Contains(characterBody) && characterBody.teamComponent.teamIndex != TeamIndex.Player)
            {
                return false;
            }
            return orig(self, characterBody);
        }
    }
}
