using RoR2;
using RoR2.Orbs;
using UnityEngine;

namespace RandomEvents
{
    public class ExperimentalDisposableMissileLauncher : ExperimentalEquipment
    {
        // per missile, as a multiple of the player's damage
        private const float ShrimpMissileDamage = 30f;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.CommandMissile;
        public override float CooldownScale => 0.25f;
        public override string Description => "Disposable Missile Launcher fires a Plasma Shrimp missile at every monster";
        public override string PopupDescription => "Fires a Plasma Shrimp missile at every monster.";

        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
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
    }
}
