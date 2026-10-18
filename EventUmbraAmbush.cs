using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RandomEvents
{
    public class EventUmbraAmbush : AbstractEvent
    {
        // Items that suit each survivor. A hunter gets a kit with half as many items of each tier as the player it
        // hunts has (setting Item Multiplier), picked from its survivor's list, or from the default list if that has
        // none of the tier, or else from all safe items of the tier. Only safe items go into the kit, see the setting
        // Unsafe Items.
        private static readonly Dictionary<string, string> DefaultSurvivorItems = new Dictionary<string, string>
        {
            { "Default", "CritGlasses, Hoof, Bear, Crowbar, BossDamageBonus, StickyBomb, ArmorPlate, FlatHealth, Tooth, ChainLightning, Missile, Seed, Infusion, IceRing, FireRing, SprintArmor, ExplodeOnDeath, Behemoth, ShockNearby, BounceNearby, Dagger, BarrierOnOverHeal, Knurl, SprintWisp, LightningStrikeOnHit, FireballsOnHit, BleedOnHitAndExplode" },
            { "CommandoBody", "CritGlasses, BossDamageBonus, StickyBomb, ChainLightning, Missile, Behemoth, BounceNearby, LightningStrikeOnHit" },
            { "HuntressBody", "CritGlasses, Hoof, Missile, ChainLightning, Dagger, BounceNearby, HeadHunter, LightningStrikeOnHit" },
            { "Bandit2Body", "CritGlasses, Crowbar, BleedOnHit, SecondarySkillMagazine, ExecuteLowHealthElite, Behemoth, Dagger, BleedOnHitAndExplode" },
            { "MercBody", "Hoof, BleedOnHit, Bear, Seed, ChainLightning, ShockNearby, BleedOnHitAndExplode, SprintWisp" },
            { "LoaderBody", "ArmorPlate, Bear, Crowbar, FlatHealth, Infusion, SprintArmor, Thorns, BarrierOnOverHeal, ShockNearby, Knurl" },
            { "CrocoBody", "Hoof, Bear, Tooth, Seed, Infusion, ChainLightning, Behemoth, ShockNearby, Knurl" },
            { "MageBody", "CritGlasses, IgniteOnKill, FireRing, IceRing, ChainLightning, Missile, Behemoth, Icicle, FireballsOnHit" },
            { "EngiBody", "ArmorPlate, Bear, ChainLightning, Missile, Thorns, ShockNearby, Knurl" },
            { "ToolbotBody", "CritGlasses, StickyBomb, ChainLightning, Missile, Behemoth, BounceNearby, Knurl" },
            { "TreebotBody", "FlatHealth, Tooth, Seed, Infusion, IceRing, Behemoth, BarrierOnOverHeal, Knurl" },
            { "CaptainBody", "CritGlasses, StickyBomb, ChainLightning, Missile, Behemoth, ShockNearby, LightningStrikeOnHit" },
            { "RailgunnerBody", "CritGlasses, BossDamageBonus, Crowbar, Missile, Behemoth, Dagger, LightningStrikeOnHit" },
            { "VoidSurvivorBody", "CritGlasses, ChainLightning, Behemoth, ShockNearby, Knurl" },
            { "SeekerBody", "Hoof, Bear, ChainLightning, Seed, BarrierOnOverHeal, ShockNearby, SprintWisp" },
            { "FalseSonBody", "ArmorPlate, FlatHealth, Bear, Infusion, Thorns, BarrierOnOverHeal, Knurl" },
            { "ChefBody", "IgniteOnKill, StickyBomb, FireRing, ChainLightning, Behemoth, FireballsOnHit" },
        };
        // the tiers whose item counts measure a player's power; void items count as their normal tier
        private static readonly Dictionary<ItemTier, ItemTier> TierOfCount = new Dictionary<ItemTier, ItemTier>
        {
            { ItemTier.Tier1, ItemTier.Tier1 }, { ItemTier.VoidTier1, ItemTier.Tier1 },
            { ItemTier.Tier2, ItemTier.Tier2 }, { ItemTier.VoidTier2, ItemTier.Tier2 },
            { ItemTier.Tier3, ItemTier.Tier3 }, { ItemTier.VoidTier3, ItemTier.Tier3 },
            { ItemTier.Boss, ItemTier.Boss }, { ItemTier.VoidBoss, ItemTier.Boss },
        };
        // never in a kit, since they would undo the hunters' slowdown
        private static readonly string[] FasterAttackItems = { "Syringe", "AttackSpeedOnCrit", "AlienHead" };
        private ConfigEntry<float> HealthMultiplier;
        private ConfigEntry<float> ItemMultiplier;
        private ConfigEntry<float> DamageMultiplier;
        private ConfigEntry<float> AttackSpeedMultiplier;
        private ConfigEntry<float> MoveSpeedMultiplier;
        private readonly Dictionary<string, ConfigEntry<string>> survivorItems = new Dictionary<string, ConfigEntry<string>>();
        private readonly List<CharacterMaster> hunters = new List<CharacterMaster>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // its faster attacks would undo the hunters' slowdown
            return GetSurvivorCandidates().Count > 0 && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("BulletHell", System.StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "UmbraAmbush";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_UMBRA_AMBUSH");
        }
        public override string GetDescription()
        {
            return "Each player is hunted by a tough enemy survivor with items, which attacks and moves slowly.";
        }
        public override string GetConditionDescription()
        {
            return "Event \"BulletHell\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HealthMultiplier = config.Bind(GetEventConfigName(), "Health Multiplier", 10f, "The hunters' health, relative to their survivor's normal health.");
            DamageMultiplier = config.Bind(GetEventConfigName(), "Damage Multiplier", 0.25f, "The damage the hunters deal, relative to their survivor's normal damage.");
            AttackSpeedMultiplier = config.Bind(GetEventConfigName(), "Attack Speed Multiplier", 0.25f, "How often the hunters attack, relative to normal: it multiplies their attack speed and divides their cooldowns.");
            MoveSpeedMultiplier = config.Bind(GetEventConfigName(), "Move Speed Multiplier", 0.5f, "The hunters' movement speed, relative to normal.");
            ItemMultiplier = config.Bind(GetEventConfigName(), "Item Multiplier", 0.5f, "The number of items per tier in a hunter's kit, relative to the player it hunts.");
            foreach (var (survivor, items) in DefaultSurvivorItems)
            {
                survivorItems[survivor] = config.Bind(GetEventConfigName(), $"Hunter Items {survivor}", items, survivor == "Default"
                    ? "Comma-separated internal item names for the hunters of survivors without their own list, and for tiers that a survivor's list lacks. Unsafe items are left out."
                    : $"Comma-separated internal item names that suit a hunter of {survivor}. Unsafe items are left out.");
            }
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
        }

        // the survivors that the game has an AI for and that the run's expansions allow
        private static List<SurvivorDef> GetSurvivorCandidates()
        {
            return SurvivorCatalog.orderedSurvivorDefs.Where(survivorDef =>
            {
                if (survivorDef == null || survivorDef.hidden || survivorDef.bodyPrefab == null)
                {
                    return false;
                }
                var expansion = survivorDef.GetRequiredExpansion();
                if (expansion != null && (Run.instance == null || !Run.instance.IsExpansionEnabled(expansion)))
                {
                    return false;
                }
                return MasterCatalog.FindAiMasterIndexForBody(survivorDef.bodyPrefab.GetComponent<CharacterBody>().bodyIndex) != MasterCatalog.MasterIndex.none;
            }).ToList();
        }

        private List<ItemIndex> ParseItems(string survivor)
        {
            var result = new List<ItemIndex>();
            if (!survivorItems.TryGetValue(survivor, out var items))
            {
                return result;
            }
            foreach (var name in items.Value.Split(','))
            {
                var itemIndex = ItemCatalog.FindItemIndex(name.Trim());
                if (itemIndex != ItemIndex.None && !Run.instance.IsItemExpansionLocked(itemIndex))
                {
                    result.Add(itemIndex);
                }
            }
            return result;
        }

        // the player's items per tier, which the hunter's kit gets as many of
        private static Dictionary<ItemTier, int> CountItemsPerTier(Inventory inventory)
        {
            var counts = new Dictionary<ItemTier, int>();
            var stacks = new int[ItemCatalog.itemCount];
            inventory.WriteAllPermanentItemStacks(stacks);
            for (int i = 0; i < stacks.Length; i++)
            {
                var itemDef = ItemCatalog.GetItemDef((ItemIndex)i);
                if (stacks[i] > 0 && itemDef != null && !itemDef.hidden && TierOfCount.TryGetValue(itemDef.tier, out var tier))
                {
                    counts[tier] = counts.GetValueOrDefault(tier) + stacks[i];
                }
            }
            return counts;
        }

        private void GiveItems(Inventory hunterInventory, string survivor, Dictionary<ItemTier, int> counts)
        {
            var safeItems = new HashSet<ItemIndex>(SafeItems.Get());
            safeItems.ExceptWith(FasterAttackItems.Select(ItemCatalog.FindItemIndex));
            var survivorList = ParseItems(survivor).Where(safeItems.Contains).ToList();
            var defaultList = ParseItems("Default").Where(safeItems.Contains).ToList();
            foreach (var (tier, count) in counts)
            {
                var candidates = survivorList.Where(itemIndex => ItemCatalog.GetItemDef(itemIndex).tier == tier).ToList();
                if (candidates.Count == 0)
                {
                    candidates = defaultList.Where(itemIndex => ItemCatalog.GetItemDef(itemIndex).tier == tier).ToList();
                }
                if (candidates.Count == 0)
                {
                    candidates = safeItems.Where(itemIndex => ItemCatalog.GetItemDef(itemIndex).tier == tier).ToList();
                }
                if (candidates.Count == 0)
                {
                    continue;
                }
                var amount = Mathf.RoundToInt(count * Mathf.Max(0f, ItemMultiplier.Value));
                for (int i = 0; i < amount; i++)
                {
                    hunterInventory.GiveItemPermanent(candidates[RoR2Application.rng.RangeInt(0, candidates.Count)]);
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            hunters.Clear();
            var survivors = GetSurvivorCandidates();
            if (survivors.Count == 0)
            {
                return;
            }
            foreach (var target in Helper.GetLivingPlayerBodies())
            {
                var survivorDef = survivors[RoR2Application.rng.RangeInt(0, survivors.Count)];
                var body = survivorDef.bodyPrefab.GetComponent<CharacterBody>();
                var masterPrefab = MasterCatalog.GetMasterPrefab(MasterCatalog.FindAiMasterIndexForBody(body.bodyIndex));
                var position = Helper.FindGroundPositionNear(target.footPosition, 20f, 40f) ?? target.footPosition;
                var hunter = new MasterSummon
                {
                    masterPrefab = masterPrefab,
                    position = position,
                    rotation = Util.QuaternionSafeLookRotation(target.footPosition - position),
                    teamIndexOverride = TeamIndex.Monster,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true
                }.Perform();
                if (hunter == null)
                {
                    continue;
                }
                // each Boost Hp adds 10% of the base health
                var boostHp = Mathf.RoundToInt((HealthMultiplier.Value - 1f) * 10f);
                if (boostHp > 0)
                {
                    hunter.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp, boostHp);
                }
                // an item without a name, which lowers the damage by 1% per stack
                var reduceDamage = Mathf.RoundToInt((1f - Mathf.Clamp01(DamageMultiplier.Value)) * 100f);
                if (reduceDamage > 0)
                {
                    hunter.inventory.GiveItemPermanent(DLC4Content.Items.ReduceDamage, reduceDamage);
                }
                GiveItems(hunter.inventory, body.name, CountItemsPerTier(target.inventory));
                var hunterBody = hunter.GetBody();
                if (hunterBody != null)
                {
                    Slowdown.Apply(hunterBody, AttackSpeedMultiplier.Value, MoveSpeedMultiplier.Value);
                }
                JoinMonsters(hunter);
                foreach (var ai in hunter.AiComponents)
                {
                    if (ai != null && ai.currentEnemy != null)
                    {
                        ai.currentEnemy.gameObject = target.gameObject;
                        ai.currentEnemy.bestHurtBox = target.mainHurtBox;
                        ai.enemyAttention = ai.enemyAttentionDuration;
                    }
                }
                hunters.Add(hunter);
            }
        }

        // Like the other monsters: in Simulacrum, they belong to the wave, which needs them killed and gives them its
        // items; otherwise they get the items of the Artifact of Evolution.
        private static void JoinMonsters(CharacterMaster hunter)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null && infiniteTowerRun.waveController.combatSquad != null)
            {
                infiniteTowerRun.waveController.combatSquad.AddMember(hunter);
            }
            else if (RunArtifactManager.instance != null && RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.MonsterTeamGainsItems)
                && RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory != null)
            {
                hunter.inventory.AddItemsFrom(RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory);
            }
        }

        public override void Stop()
        {
            // removed without dying, so they drop no items
            foreach (var hunter in hunters)
            {
                if (hunter != null)
                {
                    hunter.DestroyBody();
                    GameObject.Destroy(hunter.gameObject);
                }
            }
            hunters.Clear();
        }
    }
}
