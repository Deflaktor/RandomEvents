using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RandomEvents
{
    public class ModConfig
    {
        public static ConfigEntry<bool> Enabled { get; set; }
        public static ConfigEntry<float> EventProbability { get; set; }
        public static ConfigEntry<float> EventProbabilityIncrease { get; set; }
        public static ConfigEntry<float> SingleEventWeight { get; set; }
        public static ConfigEntry<float> DoubleEventWeight { get; set; }
        public static ConfigEntry<float> TripleEventWeight { get; set; }
        public static ConfigEntry<float> EventFrequency { get; set; }
        public static ConfigEntry<float> EventDuration { get; set; }
        public static ConfigEntry<string> UnsafeItems { get; set; }

        public static void Init()
        {
            var config = RandomEvents.instance.Config;

            Enabled = config.Bind("00 Main", "Enabled", true, "Enable Mod");
            EventProbability = config.Bind("00 Main", "Event Probability", 0.20f, "Probability for an event to occur.");
            EventProbabilityIncrease = config.Bind("00 Main", "Event Probability Increase", 0.20f, "Increases the probability each time an event does NOT occur. Until an event occurs, at which point the probability is reset.");
            SingleEventWeight = config.Bind("00 Main", "Single Event Weight", 0.60f, "Weight for a single event to occur.");
            DoubleEventWeight = config.Bind("00 Main", "Double Event Weight", 0.30f, "Weight for a double event to occur.");
            TripleEventWeight = config.Bind("00 Main", "Triple Event Weight", 0.10f, "Weight for a triple event to occur.");
            EventFrequency = config.Bind("00 Main", "Event Timer", 60f, "Only Main Mode: Do an event check every x seconds. Minimum is 5 seconds.");
            if(EventFrequency.Value < 5f)
            {
                EventFrequency.Value = 5f;
            }
            EventDuration = config.Bind("00 Main", "Event Duration", 45f, "Only Main Mode: How long the event lasts. In seconds.");
            UnsafeItems = config.Bind("00 Main", "Unsafe Items", "BarrierOnKill, Bear, BossDamageBonus, Firework, GoldOnHurt, Hoof, Medkit, Mushroom, ScrapWhite, Tooth, TreasureCache, WardOnLevel, HealingPotion, FragileDamageBonus, IncreaseDamageOnMultiKill, SpeedBoostPickup, Crowbar, NearbyDamageBonus, CritGlasses, StickyBomb, StunChanceOnHit, BleedOnHit, IgniteOnKill, SprintBonus, SecondarySkillMagazine, ArmorPlate, CritAtLowerElevation, Bandolier, BonusGoldPackOnKill, ChainLightning, EnergizedOnEquipmentUse, ExecuteLowHealthElite, ExplodeOnDeath, FreeChest, Infusion, JumpBoost, Missile, MoveSpeedOnKill, PrimarySkillShuriken, RegeneratingScrap, ScrapGreen, Squid, StrengthenBurn, TPHealingNova, Thorns, ExtraShrineItem, IncreasePrimaryDamage, ExtraStatsOnLevelUp, LowerPricedChests, TeleportOnLowHealth, HalfAttackSpeedHalfCooldowns, DeathMark, HealOnCrit, FireRing, AttackSpeedOnCrit, SprintArmor, IceRing, TriggerEnemyDebuffs, WarCryOnMultiKill, Feather, KnockBackHitEnemies, SprintOutOfCombat, EquipmentMagazine, SpeedOnPickup, ShieldBooster, Behemoth, BounceNearby, CaptainDefenseMatrix, Dagger, DroneWeapons, ExtraLife, FallBoots, GhostOnKill, HeadHunter, Icicle, ImmuneToDebuff, KillEliteFrenzy, LaserTurbine, MoreMissile, NovaOnHeal, PermanentDebuffOnHit, Plant, RandomEquipmentTrigger, ScrapRed, ShockNearby, Talisman, ItemDropChanceOnKill, BarrageOnBoss, CritDamage, UtilitySkillMagazine, MeteorAttackOnHighDamage, ArmorReductionOnHit, JumpDamageStrike, PhysicsProjectile, ArtifactKey, BeetleGland, BleedOnHitAndExplode, FireballsOnHit, LightningStrikeOnHit, MinorConstructOnKill, RoboBallBuddy, ScrapYellow, SiphonOnLowHealth, SprintWisp, TitanGoldDuringTP, AutoCastEquipment, FocusConvergence, GoldOnHit, LunarDagger, LunarPrimaryReplacement, LunarSecondaryReplacement, LunarSpecialReplacement, LunarTrinket, LunarUtilityReplacement, MonstersOnShrineUse, RandomDamageZone, RandomlyLunar, RepeatHeal, ShieldOnly, VoidMegaCrabItem, CritGlassesVoid, TreasureCacheVoid, ChainLightningVoid, ElementalRingVoid, ExplodeOnDeathVoid, MissileVoid, SlowOnHitVoid, CloverVoid, ExtraLifeVoid, BleedOnHitVoid, MushroomVoid", "Items events never give to monsters, e.g. because they could kill players unfairly. Internal names, separated by commas. The default is the ban list of Artifact Evolution Plus Plus.");
        }

        public static void InLobbyConfig()
        {
            if (ModCompatibilityInLobbyConfig.enabled)
            {
                var config = RandomEvents.instance.Config;
                ModCompatibilityInLobbyConfig.CreateFromBepInExConfigFile(config, "Random Events");
            }
        }
    }
}