using RoR2;
using UnityEngine;

namespace RandomEvents
{
    public class ExperimentalSuperMassiveLeech : ExperimentalEquipment
    {
        private const float LeechDuration = 8f;
        // a hemorrhage stack for each 100% of the player's damage a hit deals, crits counting double
        private const int MaxStacksPerHit = 20;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.LifestealOnHit;
        public override float CooldownScale => 0.25f;
        public override string Description => "Super Massive Leech causes hemorrhage instead of healing, the more the harder the hit";
        public override string PopupDescription => "Your hits cause hemorrhage instead of healing you, the more the harder they hit.";

        public override void Hook()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
        }

        // Not the leech's own buff, which would heal on hit. This one does nothing itself, but shows a red glow
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            var effectData = new EffectData
            {
                origin = body.corePosition
            };
            effectData.SetNetworkedObjectReference(body.gameObject);
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/LifeStealOnHitActivation"), effectData, transmit: true);
            body.AddTimedBuff(DLC3Content.Buffs.ShieldTankDamage, LeechDuration);
            return true;
        }

        private void GlobalEventManager_OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            var attackerBody = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (IsExperimental(attackerBody) && attackerBody.HasBuff(DLC3Content.Buffs.ShieldTankDamage) && damageInfo.procCoefficient > 0f && !damageInfo.rejected && victim != null)
            {
                var damage = damageInfo.damage * (damageInfo.crit ? attackerBody.critMultiplier : 1f);
                var stacks = Mathf.Clamp(Mathf.RoundToInt(damage / Mathf.Max(1f, attackerBody.damage)), 1, MaxStacksPerHit);
                for (int i = 0; i < stacks; i++)
                {
                    DotController.InflictDot(victim, damageInfo.attacker, damageInfo.inflictedHurtbox, damageInfo.procChainMask, DotController.DotIndex.SuperBleed, 15f * damageInfo.procCoefficient);
                }
            }
        }
    }
}
