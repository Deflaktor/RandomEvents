using BepInEx.Configuration;
using HG;
using RoR2;
using RoR2.Orbs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventZombies : AbstractEvent
    {
        public ConfigEntry<float> ZombieProbability;
        private bool oneZombieCreated = false;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                // not for boss waves
                if((infiniteTowerRun.waveIndex + 1) % 5 == 0)
                {
                    return false;
                }
            }
            return true;
        }
        public override string GetEventConfigName()
        {
            return "Zombies";
        }
        public override string GetAnnouncement()
        {
            return Language.GetString("ANNOUNCE_EVENT_ZOMBIES");
        }
        public override string GetDescription()
        {
            return "Spawns some infected enemies. Infected enemies infect other nearby enemies upon death and revive once with reduced health and speed.";
        }
        public override string GetConditionDescription()
        {
            return "Currently not a boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            ZombieProbability = config.Bind(GetEventConfigName(), "Infected Probability", 0.05f, $"Probability for a monster to become an infected (the first spawned monster is guaranteed to become an infected).");
        }

        public override void Preload()
        {

        }

        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.CharacterMaster.OnBodyDeath += CharacterMaster_OnBodyDeath;
            On.RoR2.CharacterMaster.RespawnSeekerRevive += CharacterMaster_RespawnSeekerRevive;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex != TeamIndex.Player && !body.isBoss && body.master != null && body.gameObject.GetComponent<EventGhosts.GhostComponent>() == null)
            {
                if (!oneZombieCreated)
                {
                    oneZombieCreated = true;
                    body.master.gameObject.EnsureComponent<ZombieComponent>();
                }
                else
                {
                    if (Run.instance.runRNG.nextNormalizedFloat < ZombieProbability.Value)
                    {
                        body.master.gameObject.EnsureComponent<ZombieComponent>();
                    }
                }
            }
        }

        private void CharacterMaster_OnBodyDeath(On.RoR2.CharacterMaster.orig_OnBodyDeath orig, CharacterMaster self, CharacterBody body)
        {
            orig(self, body);
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && self.TryGetComponent<ZombieComponent>(out var zombieComponent))
            {
                if(zombieComponent.revived)
                {
                    GameObject.Destroy(zombieComponent);
                }
                else
                {
                    zombieComponent.InfectOtherEnemies(body);
                }
            }
        }

        private void CharacterMaster_RespawnSeekerRevive(On.RoR2.CharacterMaster.orig_RespawnSeekerRevive orig, CharacterMaster self)
        {
            orig(self);
            if (ModConfig.Enabled.Value && NetworkServer.active && self.TryGetComponent<ZombieComponent>(out var zombieComponent))
            {
                if (IsActive())
                    zombieComponent.ZombieRevive();
                else
                    self.TrueKill();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            oneZombieCreated = false;
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.teamComponent.teamIndex != TeamIndex.Player && !body.isBoss && body.gameObject.GetComponent<EventGhosts.GhostComponent>() == null && body.master != null)
                {
                    if (!oneZombieCreated)
                    {
                        oneZombieCreated = true;
                        body.master.gameObject.EnsureComponent<ZombieComponent>();
                    }
                    else
                    {
                        if (Run.instance.runRNG.nextNormalizedFloat < ZombieProbability.Value)
                        {
                            body.master.gameObject.EnsureComponent<ZombieComponent>();
                        }
                    }
                }
            }
        }


        public override void Stop()
        {
            foreach (var master in CharacterMaster.instancesList)
            {
                if (master != null)
                {
                    if (master.TryGetComponent(out ZombieComponent zombie))
                    {
                        var body = master.GetBody();
                        if (body != null)
                        {
                            body.RemoveBuff(DLC1Content.Buffs.VoidSurvivorCorruptMode);
                        }
                    }
                }
            }
        }

        public class ZombieComponent : MonoBehaviour
        {
            public bool revived = false;
            private void OnEnable()
            {
                var master = GetComponent<CharacterMaster>();
                master.seekerSelfRevive = true;
                var body = master.GetBody();
                body.AddBuff(DLC1Content.Buffs.VoidSurvivorCorruptMode);
                //body.AddBuff(RoR2Content.Buffs.ClayGoo);
            }

            public void ZombieRevive()
            {
                var master = GetComponent<CharacterMaster>();
                var body = master.GetBody();
                master.inventory.GiveItemPermanent(RoR2Content.Items.HealthDecay.itemIndex, 60);
                master.inventory.GiveItemPermanent(RoR2Content.Items.TonicAffliction.itemIndex, 10);
                //master.inventory.GiveItem(RoR2Content.Items.Pearl.itemIndex, 10);
                // master.inventory.GiveItem(RoR2Content.Items.AdaptiveArmor.itemIndex, 1);
                //body.AddBuff(RoR2Content.Buffs.PermanentCurse);
                //body.AddBuff(RoR2Content.Buffs.ClayGoo);
                body.AddBuff(DLC1Content.Buffs.VoidSurvivorCorruptMode);
                body.RemoveBuff(RoR2Content.Buffs.Immune);
                revived = true;
            }

            private void OnDisable()
            {
                var master = GetComponent<CharacterMaster>();
                master.seekerSelfRevive = false;
                var body = master.GetBody();
                if (body != null)
                {
                    body.RemoveBuff(DLC1Content.Buffs.VoidSurvivorCorruptMode);
                }
                if (revived) { 
                    master.inventory.RemoveItemPermanent(RoR2Content.Items.HealthDecay.itemIndex, 60);
                    master.inventory.RemoveItemPermanent(RoR2Content.Items.TonicAffliction.itemIndex, 10);
                }
            }

            public void InfectOtherEnemies(CharacterBody victimBody)
            {
                CharacterMaster master = victimBody.master;
                VineOrb.SplitDebuffInformation item = new VineOrb.SplitDebuffInformation
                {
                    attacker = base.gameObject,
                    attackerMaster = master,
                    index = DLC1Content.Buffs.VoidSurvivorCorruptMode.buffIndex,
                    isTimed = false,
                    duration = 0f,
                    count = 1
                };
                List<VineOrb.SplitDebuffInformation> list = new List<VineOrb.SplitDebuffInformation>() { item };
                SphereSearch sphereSearch = new SphereSearch();
                List<HurtBox> list2 = CollectionPool<HurtBox, List<HurtBox>>.RentCollection();
                sphereSearch.mask = LayerIndex.entityPrecise.mask;
                sphereSearch.origin = victimBody.gameObject.transform.position;
                sphereSearch.radius = 20f;
                sphereSearch.queryTriggerInteraction = QueryTriggerInteraction.UseGlobal;
                sphereSearch.RefreshCandidates();
                sphereSearch.FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(TeamIndex.Player));
                sphereSearch.OrderCandidatesByDistance();
                sphereSearch.FilterCandidatesByDistinctHurtBoxEntities();
                sphereSearch.GetHurtBoxes(list2);
                sphereSearch.ClearCandidates();
                int num = 2;
                for (int j = 0; j < list2.Count; j++)
                {
                    HurtBox hurtBox = list2[j];
                    CharacterBody body = hurtBox.healthComponent.body;
                    if ((bool)hurtBox && (bool)hurtBox.healthComponent && hurtBox.healthComponent.alive && body != victimBody && body != this && body.master.GetComponent<ZombieComponent>() == null)
                    {
                        CreateVineOrbChain(victimBody.gameObject, hurtBox, list);
                        body.master.gameObject.EnsureComponent<ZombieComponent>();
                        num--;
                        if (num == 0)
                        {
                            return;
                        }
                    }
                }
                CollectionPool<HurtBox, List<HurtBox>>.ReturnCollection(list2);
            }

            private void CreateVineOrbChain(GameObject sourceGameObject, HurtBox targetHurtbox, List<VineOrb.SplitDebuffInformation> debuffInfoList)
            {
                VineOrb vineOrb = new VineOrb();
                vineOrb.origin = sourceGameObject.transform.position;
                vineOrb.target = targetHurtbox;
                vineOrb.splitDebuffInformation = debuffInfoList;
                OrbManager.instance.AddOrb(vineOrb);
            }

        }
    }
}
