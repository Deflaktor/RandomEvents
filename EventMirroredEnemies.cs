using BepInEx.Configuration;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Orbs;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventMirroredEnemies : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterDamageTaken;
        public override EventTags ExcludedTags => EventTags.MonsterDamageTaken;
        private ConfigEntry<float> MirrorChance;
        private ConfigEntry<float> HealthLossPerHit;
        private ConfigEntry<float> HealMultiplier;
        private ConfigEntry<float> ReflectedDamageCap;
        private ConfigEntry<float> ReflectedDamageMinimum;
        private ConfigEntry<int> DecayTime;
        private static readonly HashSet<CharacterBody> mirrored = new HashSet<CharacterBody>();
        // the monsters that spawned others for themselves, e.g. a Xi Construct its Alpha Constructs, which are mirrored
        // only along with them; kept also outside the event, for the ones already there when it starts
        private readonly Dictionary<CharacterMaster, CharacterBody> summoners = new Dictionary<CharacterMaster, CharacterBody>();
        // the shattering glass of a brittle death, at most this often per mirrored monster
        private const float ShatterInterval = 0.3f;
        private AsyncOperationHandle<GameObject> ShatterEffect;
        private readonly Dictionary<CharacterBody, float> lastShatter = new Dictionary<CharacterBody, float>();
        // when each mirrored monster's decay last sped up in the void fog
        private readonly Dictionary<CharacterBody, float> lastFogDecay = new Dictionary<CharacterBody, float>();
        // seconds taken off a mirrored monster's decay for each second in the void fog, down to the shortest decay
        private const int FogDecaySpeedup = 5;
        private const int ShortestDecayTime = 5;
        // the share of their health each player was dealt back in the current second, which is capped
        private readonly Dictionary<CharacterBody, (float start, float fraction)> reflectedThisSecond = new Dictionary<CharacterBody, (float start, float fraction)>();

        public override bool LoadCondition()
        {
            return true;
        }
        // both change how monsters take damage
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return true;
        }
        public override string GetEventConfigName()
        {
            return "MirroredEnemies";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_MIRRORED_ENEMIES");
        }
        public override string GetDescription()
        {
            return "Some monsters are mirrored: their health decays, their hits heal players and hurt themselves, and players' direct hits on them hurt the players instead, by the same share of health, at least 5% and up to 20% per second.";
        }
        public override string GetPopupDescription()
        {
            return "Mirrored monsters decay, and their hits heal you. Don't shoot them, it hurts you instead!";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            MirrorChance = config.Bind(GetEventConfigName(), "Mirror Chance", 0.3f, "Chance for a monster to be mirrored.");
            HealthLossPerHit = config.Bind(GetEventConfigName(), "Health Loss Per Hit", 0.2f, "How much of its maximum health a mirrored monster loses with each hit on a player.");
            HealMultiplier = config.Bind(GetEventConfigName(), "Heal Multiplier", 1f, "How much a player is healed by a mirrored monster's hit, relative to the damage it would have dealt.");
            DecayTime = config.Bind(GetEventConfigName(), "Decay Time", 60, "In how many seconds a mirrored monster's health decays from full to nothing, like a ghost's. Ghosts take 30. 0 for no decay.");
            ReflectedDamageMinimum = config.Bind(GetEventConfigName(), "Reflected Damage Minimum", 0.05f, "A player who hits a mirrored monster directly loses at least this share of their health per second, however small the hits.");
            ReflectedDamageCap = config.Bind(GetEventConfigName(), "Reflected Damage Cap", 0.2f, "A player's direct hit on a mirrored monster takes the same share of the player's health as it would have taken of the monster's, up to this share per second. Mirrored monsters take no damage from players.");
        }
        public override void Preload()
        {
            ShatterEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/BrittleDeath.prefab");
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.BullseyeSearch.RefreshCandidates += BullseyeSearch_RefreshCandidates;
            On.RoR2.SphereSearch.SearchData.FilterByHurtBoxTeam += SearchData_FilterByHurtBoxTeam;
            On.RoR2.SiphonNearbyController.SearchForTargets += SiphonNearbyController_SearchForTargets;
            On.RoR2.CharacterAI.BaseAI.ManagedFixedUpdate += BaseAI_ManagedFixedUpdate;
            On.RoR2.MasterSpawnSlotController.SpawnRandomOpen += MasterSpawnSlotController_SpawnRandomOpen;
            On.RoR2.MasterSpawnSlotController.SpawnAllOpen += MasterSpawnSlotController_SpawnAllOpen;
        }

        // Searches for the players' enemies, which leave out the players' team, skip mirrored monsters: player allies
        // picking an enemy, and items that find their own targets, e.g. Ukulele, AtG Missiles or Ceremonial Daggers.
        // These run on the host.
        private void BullseyeSearch_RefreshCandidates(On.RoR2.BullseyeSearch.orig_RefreshCandidates orig, BullseyeSearch self)
        {
            orig(self);
            if (mirrored.Count > 0 && IsActive() && !self.teamMaskFilter.HasTeam(TeamIndex.Player))
            {
                foreach (var body in mirrored)
                {
                    if (body != null)
                    {
                        self.FilterOutGameObject(body.gameObject);
                    }
                }
            }
        }

        // the same for the other kind of search, e.g. of the projectiles that find their own target
        private void SearchData_FilterByHurtBoxTeam(On.RoR2.SphereSearch.SearchData.orig_FilterByHurtBoxTeam orig, ref SphereSearch.SearchData self, TeamMask teamMask)
        {
            orig(ref self, teamMask);
            if (mirrored.Count > 0 && IsActive() && !teamMask.HasTeam(TeamIndex.Player))
            {
                for (int i = self.candidatesCount - 1; i >= 0; i--)
                {
                    var hurtBox = self.GetCandidate(i).hurtBox;
                    if (hurtBox != null && hurtBox.healthComponent != null && mirrored.Contains(hurtBox.healthComponent.body))
                    {
                        self.RemoveCandidate(i);
                    }
                }
            }
        }

        // Mired Urn's tethers too, also if its search doesn't go through the team filter. Each client's game searches for
        // the tethers it shows by itself, so there they may still show.
        private void SiphonNearbyController_SearchForTargets(On.RoR2.SiphonNearbyController.orig_SearchForTargets orig, SiphonNearbyController self, List<HurtBox> dest)
        {
            orig(self, dest);
            if (mirrored.Count > 0 && IsActive())
            {
                dest.RemoveAll(hurtBox => hurtBox != null && hurtBox.healthComponent != null && mirrored.Contains(hurtBox.healthComponent.body));
            }
        }

        // and drop one they got otherwise, e.g. by copying their leader's target or being hit by it
        private void BaseAI_ManagedFixedUpdate(On.RoR2.CharacterAI.BaseAI.orig_ManagedFixedUpdate orig, BaseAI self, float deltaTime)
        {
            if (mirrored.Count > 0 && IsActive() && self.currentEnemy != null && self.currentEnemy.characterBody != null && mirrored.Contains(self.currentEnemy.characterBody)
                && self.master != null && self.master.teamIndex == TeamIndex.Player)
            {
                self.currentEnemy.Reset();
            }
            orig(self, deltaTime);
        }

        // monsters spawning during the event may be mirrored too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                TryMirror(body);
            }
        }

        private void MasterSpawnSlotController_SpawnRandomOpen(On.RoR2.MasterSpawnSlotController.orig_SpawnRandomOpen orig, MasterSpawnSlotController self, int spawnCount, Xoroshiro128Plus rng, GameObject summonerBodyObject, Action<MasterSpawnSlotController.ISlot, SpawnCard.SpawnResult> callback)
        {
            orig(self, spawnCount, rng, summonerBodyObject, RememberSummoner(summonerBodyObject, callback));
        }

        private void MasterSpawnSlotController_SpawnAllOpen(On.RoR2.MasterSpawnSlotController.orig_SpawnAllOpen orig, MasterSpawnSlotController self, GameObject summonerBodyObject, Xoroshiro128Plus rng, Action<MasterSpawnSlotController.ISlot, SpawnCard.SpawnResult> callback)
        {
            orig(self, summonerBodyObject, rng, RememberSummoner(summonerBodyObject, callback));
        }

        private Action<MasterSpawnSlotController.ISlot, SpawnCard.SpawnResult> RememberSummoner(GameObject summonerBodyObject, Action<MasterSpawnSlotController.ISlot, SpawnCard.SpawnResult> callback)
        {
            return (slot, result) =>
            {
                if (NetworkServer.active && result.success && result.spawnedInstance != null && summonerBodyObject != null
                    && result.spawnedInstance.TryGetComponent(out CharacterMaster master) && summonerBodyObject.TryGetComponent(out CharacterBody summoner))
                {
                    summoners.Keys.Where(key => key == null).ToList().ForEach(key => summoners.Remove(key));
                    summoners[master] = summoner;
                }
                callback?.Invoke(slot, result);
            };
        }

        private bool IsSummoned(CharacterBody body)
        {
            return body != null && body.master != null && summoners.ContainsKey(body.master);
        }

        // e.g. so that they don't become ghosts as well
        public static bool IsMirrored(CharacterBody body)
        {
            return mirrored.Contains(body);
        }

        // A summoned monster is mirrored exactly when its summoner is, the others by chance. Ghosts and zombies that came
        // back are left alone: their Health Decay would add up with a mirrored monster's, and more of it decays slower.
        private void TryMirror(CharacterBody body)
        {
            if (body == null || body.healthComponent == null || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral
                || mirrored.Contains(body) || body.GetComponent<EventGhosts.GhostComponent>() != null
                || (body.master != null && body.master.TryGetComponent(out EventZombies.ZombieComponent zombie) && zombie.revived))
            {
                return;
            }
            var mirror = body.master != null && summoners.TryGetValue(body.master, out var summoner)
                ? summoner != null && mirrored.Contains(summoner)
                : RoR2Application.rng.nextNormalizedFloat < MirrorChance.Value;
            if (mirror)
            {
                mirrored.Add(body);
                if (body.inventory == null)
                {
                    return;
                }
                // the Lunar Chimera's shell, the shine of invulnerable Solus Wing parts and that of a Kinetic Dampener's
                // shield, which shows while they have shield, mark them, and their health decays like a ghost's
                body.AddBuff(RoR2Content.Buffs.LunarShell);
                body.AddBuff(DLC3Content.Buffs.SolusWingInvulnerability);
                EventLoot.GetInventory(this, body).GiveTemporaryItem(DLC3Content.Items.ShieldBooster.itemIndex);
                body.RecalculateStats();
                body.healthComponent.RechargeShieldFull();
                if (DecayTime.Value > 0)
                {
                    EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.HealthDecay.itemIndex, DecayTime.Value);
                }
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            // Only players can hurt them, so that their shield never breaks into the Kinetic Dampener's stunning pulse: the
            // void fog speeds up their decay instead, and anything else, e.g. a fall or another monster, does nothing. It
            // doesn't reach the game at all, whose Lunar Chimera's shell would turn it into 1 damage to the shield.
            if (NetworkServer.active && IsActive() && damageInfo != null && self.body != null && mirrored.Contains(self.body))
            {
                if ((damageInfo.damageType.damageTypeExtended & DamageTypeExtended.DamageField) != 0)
                {
                    SpeedUpDecay(self.body);
                }
                var hitter = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
                if (hitter == null || hitter.teamComponent.teamIndex != TeamIndex.Player)
                {
                    return;
                }
            }
            if (!NetworkServer.active || !IsActive() || damageInfo == null || damageInfo.rejected || damageInfo.damage <= 0f || damageInfo.attacker == null)
            {
                orig(self, damageInfo);
                return;
            }
            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            // only its hits, not the ticks of its burns and the like
            if (attackerBody != null && mirrored.Contains(attackerBody) && damageInfo.dotIndex == DotController.DotIndex.None && damageInfo.procChainMask.mask == 0 && self.body != null && self.body.isPlayerControlled && attackerBody.healthComponent.alive)
            {
                // the hit heals the player and hurts the monster instead
                self.Heal(damageInfo.damage * HealMultiplier.Value, damageInfo.procChainMask);
                LoseHealth(attackerBody.healthComponent, self.body);
                return;
            }
            if (attackerBody != null && mirrored.Contains(self.body) && attackerBody.teamComponent.teamIndex == TeamIndex.Player)
            {
                // the ticks of damage over time, e.g. poison or burns, pass by without a sound, a shatter or anything sent
                // back
                if (damageInfo.dotIndex != DotController.DotIndex.None || (damageInfo.damageType.damageType & DamageType.DoT) != 0)
                {
                    return;
                }
                // The monster takes nothing, and the attacker loses the share of their health that a direct hit would
                // have taken of the monster's. Blocked hits trigger no items, so item effects never started by hitting it
                // and aren't sent back.
                EffectManager.SpawnEffect(HealthComponent.AssetReferences.damageRejectedPrefab, new EffectData { origin = damageInfo.position }, true);
                Shatter(self.body, damageInfo.position);
                // items' own damage, e.g. Mired Urn's siphon, carries the item color instead of a proc
                var directHit = damageInfo.procChainMask.mask == 0 && damageInfo.dotIndex == DotController.DotIndex.None && damageInfo.damageColorIndex != DamageColorIndex.Item;
                var fraction = directHit && attackerBody.healthComponent != null && attackerBody.healthComponent.alive ? GetReflectedFraction(attackerBody, damageInfo.damage / self.fullCombinedHealth) : 0f;
                if (fraction > 0f && attackerBody.mainHurtBox != null)
                {
                    // sent back like a Plasma Shrimp's missile; marked so that it doesn't count as the monster's hit
                    var orb = new MissileVoidOrb
                    {
                        origin = damageInfo.position,
                        damageValue = fraction * attackerBody.healthComponent.fullCombinedHealth,
                        isCrit = damageInfo.crit,
                        teamIndex = self.body.teamComponent.teamIndex,
                        attacker = self.gameObject,
                        procCoefficient = 0f,
                        damageColorIndex = DamageColorIndex.Void,
                        damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                        target = attackerBody.mainHurtBox
                    };
                    orb.procChainMask.AddProc(ProcType.Missile);
                    OrbManager.instance.AddOrb(orb);
                }
                return;
            }
            orig(self, damageInfo);
        }

        // Each Health Decay stack is a second the monster takes to lose its health, so fewer stacks decay it faster
        private void SpeedUpDecay(CharacterBody body)
        {
            var now = Time.fixedTime;
            if (DecayTime.Value <= 0 || body.inventory == null || (lastFogDecay.TryGetValue(body, out var last) && now - last < 1f))
            {
                return;
            }
            lastFogDecay[body] = now;
            var inventory = EventLoot.GetInventory(this, body);
            var stacks = inventory.temporaryItems.GetValueOrDefault(RoR2Content.Items.HealthDecay.itemIndex);
            var remove = Mathf.Min(FogDecaySpeedup, stacks - ShortestDecayTime);
            if (remove > 0)
            {
                inventory.RemoveTemporaryItem(RoR2Content.Items.HealthDecay.itemIndex, remove);
            }
        }

        private void Shatter(CharacterBody body, Vector3 position)
        {
            var now = Time.fixedTime;
            if (lastShatter.TryGetValue(body, out var last) && now - last < ShatterInterval)
            {
                return;
            }
            lastShatter[body] = now;
            var prefab = ShatterEffect.WaitForCompletion();
            if (prefab != null)
            {
                EffectManager.SpawnEffect(prefab, new EffectData { origin = position, scale = body.radius }, true);
            }
        }

        // the share of health within what is left of the player's cap for the current second, and at least what is left
        // of the minimum
        private float GetReflectedFraction(CharacterBody player, float fraction)
        {
            var now = Time.fixedTime;
            if (!reflectedThisSecond.TryGetValue(player, out var reflected) || now - reflected.start >= 1f)
            {
                reflected = (now, 0f);
            }
            fraction = Mathf.Max(fraction, ReflectedDamageMinimum.Value - reflected.fraction);
            fraction = Mathf.Clamp(fraction, 0f, Mathf.Max(0f, ReflectedDamageCap.Value - reflected.fraction));
            reflectedThisSecond[player] = (reflected.start, reflected.fraction + fraction);
            return fraction;
        }

        // the health goes directly, past the shield, which stays for its shine; the player gets the kill
        private void LoseHealth(HealthComponent healthComponent, CharacterBody player)
        {
            var loss = healthComponent.fullCombinedHealth * HealthLossPerHit.Value;
            if (loss >= healthComponent.combinedHealth)
            {
                healthComponent.Suicide(player.gameObject, player.gameObject);
                return;
            }
            var fromBarrier = Mathf.Min(loss, healthComponent.barrier);
            healthComponent.Networkbarrier = healthComponent.barrier - fromBarrier;
            loss -= fromBarrier;
            healthComponent.Networkhealth = healthComponent.health - loss;
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            mirrored.Clear();
            // summoners first, whose summoned monsters follow them
            foreach (var body in CharacterBody.readOnlyInstancesList.OrderBy(IsSummoned).ToList())
            {
                TryMirror(body);
            }
        }

        public override void Stop()
        {
            reflectedThisSecond.Clear();
            lastShatter.Clear();
            lastFogDecay.Clear();
            EventLoot.Clear(this);
            // their marks go, and the Kinetic Dampener's shield with them, set directly so that it doesn't break into its pulse
            foreach (var body in mirrored)
            {
                if (body != null && body.HasBuff(RoR2Content.Buffs.LunarShell))
                {
                    body.RemoveBuff(RoR2Content.Buffs.LunarShell);
                }
                if (body != null && body.HasBuff(DLC3Content.Buffs.SolusWingInvulnerability))
                {
                    body.RemoveBuff(DLC3Content.Buffs.SolusWingInvulnerability);
                }
                if (body != null && body.healthComponent != null)
                {
                    body.RecalculateStats();
                    body.healthComponent.Networkshield = Mathf.Min(body.healthComponent.shield, body.healthComponent.fullShield);
                }
            }
            mirrored.Clear();
        }
    }
}
