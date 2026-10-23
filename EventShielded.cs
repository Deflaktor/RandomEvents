using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventShielded : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        private ConfigEntry<float> ShieldedShare;
        private ConfigEntry<float> HealthFraction;
        private ConfigEntry<float> ShieldedDamageTaken;
        private readonly BuffTracker buffs = new BuffTracker();
        private readonly HashSet<CharacterBody> shielded = new HashSet<CharacterBody>();
        // Repulsion Armor Plate's block, at most this often for each monster
        private const float BlockSoundInterval = 0.15f;
        private NetworkSoundEventDef blockSound;
        private readonly Dictionary<CharacterBody, float> lastBlockSound = new Dictionary<CharacterBody, float>();

        public override bool LoadCondition()
        {
            return true;
        }
        // not while the teleporter or a Simulacrum boss wave has a boss to fight, who could stay shielded for long;
        // both change how monsters take damage
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var teleporter = TeleporterInteraction.instance;
            return !Helper.IsSimulacrumBossWaveNext() && (teleporter == null || !(teleporter.isIdleToCharging || teleporter.isCharging))
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("MirroredEnemies", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "Shielded";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SHIELDED");
        }
        public override string GetDescription()
        {
            return "Three quarters of the monsters are shielded, with 300 more armor, and take only 5% of the damage. The others have a quarter of their health, and when one dies, the nearest shielded monster loses its shield.";
        }
        public override string GetPopupDescription()
        {
            return "Most monsters are shielded. Kill an unshielded one to break the nearest shield.";
        }
        public override string GetConditionDescription()
        {
            return "Not during the teleporter event or a Simulacrum boss wave, and event \"MirroredEnemies\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            ShieldedShare = config.Bind(GetEventConfigName(), "Shielded Share", 0.75f, "The share of the monsters that are shielded.");
            HealthFraction = config.Bind(GetEventConfigName(), "Health Fraction", 0.25f, "The share of their health the monsters that aren't shielded keep.");
            ShieldedDamageTaken = config.Bind(GetEventConfigName(), "Shielded Damage Taken", 0.05f, "The share of the damage shielded monsters take.");
        }
        public override void Preload()
        {
            blockSound = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<NetworkSoundEventDef>("RoR2/Base/ArmorPlate/nseArmorPlateBlock.asset").WaitForCompletion();
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            GlobalEventManager.onCharacterDeathGlobal += GlobalEventManager_onCharacterDeathGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && damageInfo != null && self.body != null && shielded.Contains(self.body))
            {
                damageInfo.damage *= Mathf.Clamp01(ShieldedDamageTaken.Value);
                PlayBlockSound(self.body, damageInfo);
            }
            orig(self, damageInfo);
        }

        private static bool IsMonster(CharacterBody body)
        {
            return body != null && body.healthComponent != null && body.healthComponent.alive && body.master != null
                && body.teamComponent.teamIndex != TeamIndex.Player && body.teamComponent.teamIndex != TeamIndex.Neutral && body.teamComponent.teamIndex != TeamIndex.None;
        }

        // monsters spawning during the event too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && IsMonster(body))
            {
                Assign(body);
            }
        }

        // every player hears the hit being blocked, but not every tick of damage over time
        private void PlayBlockSound(CharacterBody body, DamageInfo damageInfo)
        {
            if (blockSound == null || (damageInfo.damageType.damageType & DamageType.DoT) != 0
                || lastBlockSound.TryGetValue(body, out var last) && Time.fixedTime - last < BlockSoundInterval)
            {
                return;
            }
            lastBlockSound[body] = Time.fixedTime;
            RoR2.Audio.EntitySoundManager.EmitSoundServer(blockSound.index, body.gameObject);
        }

        private void Assign(CharacterBody body)
        {
            if (RoR2Application.rng.nextNormalizedFloat < ShieldedShare.Value)
            {
                Shield(body);
            }
            else
            {
                Weaken(body);
            }
        }

        // the host cuts the damage it takes; the shine is that of the Solus Wing's invulnerable elite, and on top the
        // armor of a Twisted elite's bead
        private void Shield(CharacterBody body)
        {
            shielded.Add(body);
            buffs.Add(body, DLC3Content.Buffs.SolusWingInvulnerability);
            buffs.Add(body, DLC2Content.Buffs.BeadArmor);
        }

        private void Weaken(CharacterBody body)
        {
            if (shielded.Remove(body))
            {
                buffs.Remove(body, DLC3Content.Buffs.SolusWingInvulnerability);
                buffs.Remove(body, DLC2Content.Buffs.BeadArmor);
            }
            var healthComponent = body.healthComponent;
            var fraction = Mathf.Clamp01(HealthFraction.Value);
            healthComponent.Networkhealth = Mathf.Max(1f, Mathf.Min(healthComponent.health, healthComponent.fullHealth * fraction));
            healthComponent.Networkshield = Mathf.Min(healthComponent.shield, healthComponent.fullShield * fraction);
        }

        // the nearest shielded monster to one that died loses its shield, shown by the vine of Zombies' infection
        private void GlobalEventManager_onCharacterDeathGlobal(DamageReport report)
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || report.victimBody == null || shielded.Contains(report.victimBody)
                || report.victimBody.teamComponent.teamIndex == TeamIndex.Player || report.victimBody.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            shielded.RemoveWhere(body => !IsMonster(body));
            var position = report.victimBody.corePosition;
            var next = shielded.OrderBy(body => (body.corePosition - position).sqrMagnitude).FirstOrDefault();
            if (next == null)
            {
                return;
            }
            if (next.mainHurtBox != null)
            {
                OrbManager.instance.AddOrb(new VineOrb
                {
                    origin = position,
                    target = next.mainHurtBox,
                    splitDebuffInformation = new List<VineOrb.SplitDebuffInformation>()
                });
            }
            Weaken(next);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            shielded.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList.Where(IsMonster).ToList())
            {
                Assign(body);
            }
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            shielded.Clear();
            lastBlockSound.Clear();
        }
    }
}
