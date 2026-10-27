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
        public override string PopupDescription => "Your hits heal you and cause hemorrhage.";

        public override void Hook()
        {
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
        }

        // the leech's own buff, which heals on hit as usual
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            var effectData = new EffectData
            {
                origin = body.corePosition
            };
            effectData.SetNetworkedObjectReference(body.gameObject);
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/LifeStealOnHitActivation"), effectData, transmit: true);
            body.AddTimedBuff(RoR2Content.Buffs.LifeSteal, LeechDuration);
            return true;
        }

        private void GlobalEventManager_OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);
            var attackerBody = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (IsExperimental(attackerBody) && attackerBody.HasBuff(RoR2Content.Buffs.LifeSteal) && damageInfo.procCoefficient > 0f && !damageInfo.rejected && victim != null)
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
