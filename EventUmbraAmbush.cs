using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RandomEvents
{
    public class EventUmbraAmbush : AbstractEvent
    {
        // Items that suit each survivor. An umbra gets as many items of each tier as the player it hunts has,
        // picked from its survivor's list, or from the default list if that has none of the tier.
        private static readonly Dictionary<string, string> DefaultSurvivorItems = new Dictionary<string, string>
        {
            { "Default", "Syringe, CritGlasses, Hoof, Bear, Crowbar, BossDamageBonus, StickyBomb, ArmorPlate, FlatHealth, Tooth, ChainLightning, Missile, AttackSpeedOnCrit, Seed, Infusion, IceRing, FireRing, SprintArmor, ExplodeOnDeath, Behemoth, ShockNearby, BounceNearby, Dagger, AlienHead, BarrierOnOverHeal, Knurl, SprintWisp, LightningStrikeOnHit, FireballsOnHit, BleedOnHitAndExplode" },
            { "CommandoBody", "Syringe, CritGlasses, BossDamageBonus, StickyBomb, ChainLightning, AttackSpeedOnCrit, Missile, Behemoth, BounceNearby, LightningStrikeOnHit" },
            { "HuntressBody", "Syringe, CritGlasses, Hoof, Missile, AttackSpeedOnCrit, ChainLightning, Dagger, BounceNearby, HeadHunter, LightningStrikeOnHit" },
            { "Bandit2Body", "CritGlasses, Crowbar, BleedOnHit, SecondarySkillMagazine, AttackSpeedOnCrit, ExecuteLowHealthElite, Behemoth, Dagger, BleedOnHitAndExplode" },
            { "MercBody", "Syringe, Hoof, BleedOnHit, Bear, Seed, AttackSpeedOnCrit, ChainLightning, ShockNearby, BleedOnHitAndExplode, SprintWisp" },
            { "LoaderBody", "ArmorPlate, Bear, Crowbar, FlatHealth, Infusion, SprintArmor, Thorns, BarrierOnOverHeal, ShockNearby, Knurl" },
            { "CrocoBody", "Syringe, Hoof, Bear, Tooth, Seed, Infusion, ChainLightning, Behemoth, ShockNearby, Knurl" },
            { "MageBody", "CritGlasses, IgniteOnKill, FireRing, IceRing, ChainLightning, Missile, Behemoth, Icicle, FireballsOnHit, AlienHead" },
            { "EngiBody", "ArmorPlate, Bear, Syringe, ChainLightning, Missile, Thorns, AlienHead, ShockNearby, Knurl" },
            { "ToolbotBody", "Syringe, CritGlasses, StickyBomb, ChainLightning, AttackSpeedOnCrit, Missile, Behemoth, BounceNearby, Knurl" },
            { "TreebotBody", "FlatHealth, Tooth, Seed, Infusion, IceRing, Behemoth, BarrierOnOverHeal, Knurl" },
            { "CaptainBody", "CritGlasses, StickyBomb, ChainLightning, Missile, AttackSpeedOnCrit, Behemoth, ShockNearby, LightningStrikeOnHit" },
            { "RailgunnerBody", "CritGlasses, BossDamageBonus, Crowbar, AttackSpeedOnCrit, Missile, Behemoth, Dagger, LightningStrikeOnHit" },
            { "VoidSurvivorBody", "Syringe, CritGlasses, ChainLightning, AttackSpeedOnCrit, Behemoth, ShockNearby, Knurl" },
            { "SeekerBody", "Syringe, Hoof, Bear, ChainLightning, Seed, BarrierOnOverHeal, ShockNearby, SprintWisp" },
            { "FalseSonBody", "ArmorPlate, FlatHealth, Bear, Infusion, Thorns, BarrierOnOverHeal, Knurl" },
            { "ChefBody", "IgniteOnKill, StickyBomb, FireRing, ChainLightning, Behemoth, FireballsOnHit" },
        };
        // the tiers whose items the umbras copy the count of; void items count as their normal tier
        private static readonly Dictionary<ItemTier, ItemTier> TierOfCount = new Dictionary<ItemTier, ItemTier>
        {
            { ItemTier.Tier1, ItemTier.Tier1 }, { ItemTier.VoidTier1, ItemTier.Tier1 },
            { ItemTier.Tier2, ItemTier.Tier2 }, { ItemTier.VoidTier2, ItemTier.Tier2 },
            { ItemTier.Tier3, ItemTier.Tier3 }, { ItemTier.VoidTier3, ItemTier.Tier3 },
            { ItemTier.Boss, ItemTier.Boss }, { ItemTier.VoidBoss, ItemTier.Boss },
        };
        // the umbra item makes them deal 4% of their damage, as Artifact of Vengeance's umbras that copy all of a player's items
        private const float UmbraItemDamage = 0.04f;
        private ConfigEntry<float> UmbraDamage;
        private readonly Dictionary<string, ConfigEntry<string>> survivorItems = new Dictionary<string, ConfigEntry<string>>();
        private readonly List<CharacterMaster> umbras = new List<CharacterMaster>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetSurvivorCandidates().Count > 0;
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
            return "Each player is hunted by the umbra of a random survivor. It has ten times the health and as many items per tier as that player, picked to suit its survivor. Killing it drops an item. The umbras disappear when the event ends.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            UmbraDamage = config.Bind(GetEventConfigName(), "Umbra Damage", 1f, "The umbras' damage, relative to their survivor's normal damage. Artifact of Vengeance's umbras deal 0.04.");
            foreach (var (survivor, items) in DefaultSurvivorItems)
            {
                survivorItems[survivor] = config.Bind(GetEventConfigName(), $"Umbra Items {survivor}", items, survivor == "Default"
                    ? "Comma-separated internal item names for the umbras of survivors without their own list, and for tiers that a survivor's list lacks."
                    : $"Comma-separated internal item names that suit the umbra of {survivor}.");
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

        // the player's items per tier, which the umbra gets as many of
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

        private void GiveItems(Inventory umbraInventory, string survivor, Dictionary<ItemTier, int> counts)
        {
            var survivorList = ParseItems(survivor);
            var defaultList = ParseItems("Default");
            foreach (var (tier, count) in counts)
            {
                var candidates = survivorList.Where(itemIndex => ItemCatalog.GetItemDef(itemIndex).tier == tier).ToList();
                if (candidates.Count == 0)
                {
                    candidates = defaultList.Where(itemIndex => ItemCatalog.GetItemDef(itemIndex).tier == tier).ToList();
                }
                if (candidates.Count == 0)
                {
                    continue;
                }
                for (int i = 0; i < count; i++)
                {
                    umbraInventory.GiveItemPermanent(candidates[RoR2Application.rng.RangeInt(0, candidates.Count)]);
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            umbras.Clear();
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
                var umbra = new MasterSummon
                {
                    masterPrefab = masterPrefab,
                    position = position,
                    rotation = Util.QuaternionSafeLookRotation(target.footPosition - position),
                    teamIndexOverride = TeamIndex.Monster,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true
                }.Perform();
                if (umbra == null)
                {
                    continue;
                }
                // the umbra item gives the look, the name "Umbra of ...", ten times the health and an item drop on death
                umbra.inventory.GiveItemPermanent(RoR2Content.Items.InvadingDoppelganger);
                var boostDamage = Mathf.RoundToInt((UmbraDamage.Value / UmbraItemDamage - 1f) * 10f);
                if (boostDamage > 0)
                {
                    umbra.inventory.GiveItemPermanent(RoR2Content.Items.BoostDamage, boostDamage);
                }
                GiveItems(umbra.inventory, body.name, CountItemsPerTier(target.inventory));
                foreach (var ai in umbra.AiComponents)
                {
                    if (ai != null && ai.currentEnemy != null)
                    {
                        ai.currentEnemy.gameObject = target.gameObject;
                        ai.currentEnemy.bestHurtBox = target.mainHurtBox;
                        ai.enemyAttention = ai.enemyAttentionDuration;
                    }
                }
                umbras.Add(umbra);
            }
        }

        public override void Stop()
        {
            // removed without dying, so they drop no items
            foreach (var umbra in umbras)
            {
                if (umbra != null)
                {
                    umbra.DestroyBody();
                    GameObject.Destroy(umbra.gameObject);
                }
            }
            umbras.Clear();
        }
    }
}
