using BepInEx.Configuration;
using HG;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using static RandomEvents.EventHaste;

namespace RandomEvents
{
    public class EventExplodingCorpses : AbstractEvent
    {
        private AsyncOperationHandle<GameObject> FusionCellExplosion;
        private AsyncOperationHandle<GameObject> VFXScorchlingBreachExplosion;

        public override bool LoadCondition()
        {
            return true;
        }

        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Condition is that all survivors need to have a ranged attack
            var meleeOnlyCharacter = false;
            foreach (PlayerCharacterMasterController pc in PlayerCharacterMasterController.instances)
            {
                if (pc.isConnected)
                {
                    // check if character has a reliable way to do ranged attacks using items
                    if (pc.master?.inventory.GetItemCountEffective(RoR2Content.Items.LunarPrimaryReplacement) >= 1)
                        continue;
                    if (pc.master?.inventory.GetItemCountEffective(DLC1Content.Items.PrimarySkillShuriken) >= 8)
                        continue;
                    if (pc.master?.inventory.GetItemCountEffective(RoR2Content.Items.SprintWisp) >= 2)
                        continue;
                    // if enough items have been collected, we assume there is a way to stay out of danger
                    var inventoryPower = pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.Tier1) +
                                         pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.Tier2) * 2 +
                                         pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.Tier3) * 7 +
                                         pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.Boss) * 8 +
                                         pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.VoidTier1) * 2 +
                                         pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.VoidTier2) * 3 +
                                         pc.master?.inventory.GetTotalItemCountOfTier(ItemTier.VoidTier3) * 4;
                    if(inventoryPower >= 70)
                    {
                        continue;
                    }

                    // is the survivor a melee survivor?
                    var survivorDef = SurvivorCatalog.FindSurvivorDefFromBody(pc.master?.bodyPrefab);
                    if (survivorDef != null)
                    {
                        switch(survivorDef.cachedName)
                        {
                            case "Loader":
                            case "Merc":
                            case "FalseSon":
                                meleeOnlyCharacter = true;
                                break;
                            case "Toolbot":
                                var body = pc.master.bodyPrefab.GetComponent<CharacterBody>();
                                var skillDef = SkillCatalog.GetSkillDef(SkillCatalog.FindSkillIndexByName("FireBuzzsaw"));
                                var primary1IsBuzzsaw = Helper.HasSkillVariantEnabled(pc.master.loadout, body.bodyIndex, Helper.FindSkillByFamilyName("ToolbotBodyPrimary1"), skillDef);
                                var primary2IsBuzzsaw = Helper.HasSkillVariantEnabled(pc.master.loadout, body.bodyIndex, Helper.FindSkillByFamilyName("ToolbotBodyPrimary2"), skillDef);
                                meleeOnlyCharacter |= primary1IsBuzzsaw;
                                break;
                        }
                    }
                    else
                    {
                        meleeOnlyCharacter |= true;
                    }
                }
            }
            return !meleeOnlyCharacter;
        }
        public override string GetEventConfigName()
        {
            return "ExplodingCorpses";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_EXPLODING_CORPSES");
        }
        public override string GetDescription()
        {
            return "All enemies explode upon death.";
        }
        public override string GetConditionDescription()
        {
            return "Players have sufficient means to do ranged damage.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            
        }

        public override void Preload()
        {
            FusionCellExplosion = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab");
            VFXScorchlingBreachExplosion = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Scorchling/VFXScorchlingBreachExplosion.prefab");
            // RoR2/Base/AltarSkeleton/OmniExplosionVFXAltarSkeleton.prefab
            // RoR2/Base/BleedOnHitAndExplode/BleedOnHitAndExplode_Explosion.prefab
            // RoR2/Base/Captain/CaptainAirstrikeImpact1.prefab
            // RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab <- favorite
            // RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab <- favorite
            // RoR2/Base/GreaterWisp/OmniExplosionVFXGreaterWisp.prefab <- green explosion
            // RoR2/DLC2/Scorchling/VFXScorchlingBreachExplosion.prefab <- favorite
        }

        public override void Hook()
        {
            GlobalEventManager.onDeathProcServer += GlobalEventManager_onDeathProcServer;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void GlobalEventManager_onDeathProcServer(DamageReport damageReport)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && damageReport != null && damageReport.victimBody != null && IsActive())
            {
                Detonate(damageReport.victimBody, damageReport.attacker);
            }
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive())
            {
                if (!body.HasBuff(RoR2Content.Buffs.AttackSpeedOnCrit))
                {
                    body.AddBuff(RoR2Content.Buffs.AttackSpeedOnCrit);
                }
            }
        }


        private void Detonate(CharacterBody body, GameObject attacker)
        {
            float baseMaxHealth = body.baseMaxHealth;
            // fix outliers
            if(baseMaxHealth > 5000)
            {
                baseMaxHealth = 5000;
            }
            Vector3 position = Vector3.Lerp(body.corePosition, body.footPosition, 0.5f);
            float force = baseMaxHealth * 5f;
            float damage = body.damage * 5f;
            float radius = Mathf.Max(10f, Mathf.Sqrt(baseMaxHealth) * 0.7f);
            EffectManager.SpawnEffect(VFXScorchlingBreachExplosion.WaitForCompletion(), new EffectData
            {
                origin = position,
                scale = radius * 0.75f
            }, transmit: true);
            BlastAttack blastAttack = new BlastAttack();
            blastAttack.position = position;
            blastAttack.radius = radius;
            blastAttack.falloffModel = BlastAttack.FalloffModel.SweetSpot;
            blastAttack.attacker = attacker;
            blastAttack.inflictor = null;
            blastAttack.damageColorIndex = DamageColorIndex.Default;
            //blastAttack.baseDamage = body.damage * 5f;
            blastAttack.baseDamage = damage;
            blastAttack.baseForce = force * 3f;
            blastAttack.bonusForce = Vector3.zero;
            blastAttack.attackerFiltering = AttackerFiltering.AlwaysHit;
            blastAttack.crit = false;
            blastAttack.procChainMask = default(ProcChainMask);
            blastAttack.procCoefficient = 0f;
            blastAttack.teamIndex = body.teamComponent.teamIndex;
            blastAttack.Fire();
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if(!body.HasBuff(RoR2Content.Buffs.AttackSpeedOnCrit))
                    {
                        body.AddBuff(RoR2Content.Buffs.AttackSpeedOnCrit);
                    }
                }
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.HasBuff(RoR2Content.Buffs.AttackSpeedOnCrit))
                    {
                        body.RemoveBuff(RoR2Content.Buffs.AttackSpeedOnCrit);
                    }
                }
            }
        }
    }
}
