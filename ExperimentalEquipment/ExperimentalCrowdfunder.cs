using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // The Crowdfunder fires as usual, but costs no gold, and each of its hits launches a monster like the Breaching Fin,
    // even one in the air, and makes it lighter. Once it hasn't been hit for 2 seconds, it gets three times as heavy as
    // normal, which wears off over 3 seconds. A monster it has hit dies when it leaves the map, even one that would be brought back.
    public class ExperimentalCrowdfunder : ExperimentalEquipment
    {
        // how much the gravity shrinks with each hit, down to the least
        private const float GravityStep = 0.1f;
        private const float MinGravityScale = 0.1f;
        private const float HeavyGravityScale = 3f;
        private const float HeavyDelay = 2f;
        // back to normal from three times as heavy in 3 seconds
        private const float HeavyDecayPerSecond = (HeavyGravityScale - 1f) / 3f;
        private readonly HashSet<CharacterBody> hitBodies = new HashSet<CharacterBody>();
        // when each lightened one was last hit
        private readonly Dictionary<CharacterBody, float> lastHit = new Dictionary<CharacterBody, float>();
        // those getting lighter again by themselves
        private readonly HashSet<CharacterBody> heavy = new HashSet<CharacterBody>();

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.GoldGat;
        public override string PopupDescription => "Free to use. Each hit launches a monster into the air and makes it lighter. Monsters knocked off the map die.";

        // it fires as usual, toggled by the player's own game
        public override bool ReplacesAction => false;

        public override void Hook()
        {
            On.EntityStates.GoldGat.GoldGatFire.FireBullet += GoldGatFire_FireBullet;
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
            On.RoR2.MapZone.CharacterEligibleForTeleport += MapZone_CharacterEligibleForTeleport;
            RoR2Application.onFixedUpdate += MakeHeavy;
        }

        // it only starts firing with gold
        public override void FixedUpdatePlayer(CharacterBody body)
        {
            if (body.master != null && body.master.money == 0)
            {
                body.master.money = 1;
            }
        }

        // every monster it hit gets its normal gravity back
        public override void Stop()
        {
            foreach (var body in hitBodies)
            {
                if (body != null && body.characterMotor != null)
                {
                    body.characterMotor.gravityScale = 1f;
                }
            }
            hitBodies.Clear();
            lastHit.Clear();
            heavy.Clear();
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
                || !victim.TryGetComponent(out CharacterBody body) || body.teamComponent.teamIndex == attackerBody.teamComponent.teamIndex)
            {
                return;
            }
            Launch(attackerBody, body);
            MakeLighter(body);
        }

        // its AI runs on the host, so the host's motor is the one that moves it
        private void MakeLighter(CharacterBody body)
        {
            if (body.characterMotor != null)
            {
                body.characterMotor.gravityScale = Mathf.Max(MinGravityScale, body.characterMotor.gravityScale - GravityStep);
                lastHit[body] = Time.fixedTime;
                heavy.Remove(body);
            }
            hitBodies.Add(body);
        }

        // left alone long enough, it drops, then gets back to normal
        private void MakeHeavy()
        {
            if (lastHit.Count == 0 && heavy.Count == 0 || !NetworkServer.active)
            {
                return;
            }
            foreach (var body in heavy.ToList())
            {
                if (body == null || body.characterMotor == null)
                {
                    heavy.Remove(body);
                    continue;
                }
                var motor = body.characterMotor;
                motor.gravityScale = Mathf.MoveTowards(motor.gravityScale, 1f, HeavyDecayPerSecond * Time.fixedDeltaTime);
                if (motor.gravityScale == 1f)
                {
                    heavy.Remove(body);
                }
            }
            foreach (var (body, time) in lastHit.ToList())
            {
                if (body == null || body.characterMotor == null)
                {
                    lastHit.Remove(body);
                }
                else if (Time.fixedTime - time >= HeavyDelay)
                {
                    body.characterMotor.gravityScale = HeavyGravityScale;
                    lastHit.Remove(body);
                    heavy.Add(body);
                }
            }
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
            // players are brought back as usual, e.g. those launched in Deathmatch
            if (IsActive() && characterBody != null && hitBodies.Contains(characterBody) && !characterBody.isPlayerControlled)
            {
                return false;
            }
            return orig(self, characterBody);
        }
    }
}
