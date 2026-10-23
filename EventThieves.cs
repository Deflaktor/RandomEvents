using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventThieves : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        private ConfigEntry<float> StealCooldown;
        // what each monster stole from whom
        private readonly Dictionary<CharacterMaster, List<(CharacterMaster victim, ItemIndex itemIndex)>> loot = new Dictionary<CharacterMaster, List<(CharacterMaster victim, ItemIndex itemIndex)>>();
        // when each monster can steal again
        private readonly Dictionary<CharacterMaster, float> nextStealTimes = new Dictionary<CharacterMaster, float>();
        private HashSet<ItemIndex> stealableItems = new HashSet<ItemIndex>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return true;
        }
        public override string GetEventConfigName()
        {
            return "Thieves";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_THIEVES");
        }
        public override string GetDescription()
        {
            return "Monsters that hit a player steal one of their items and use it, up to one every half second. Kill the thief to get them back.";
        }
        public override string GetPopupDescription()
        {
            return "Monsters that hit you steal your items and use them. Kill the thief to get them back!";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            StealCooldown = config.Bind(GetEventConfigName(), "Steal Cooldown", 0.5f, "Seconds before a monster can steal again.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            GlobalEventManager.onServerDamageDealt += GlobalEventManager_onServerDamageDealt;
            GlobalEventManager.onCharacterDeathGlobal += GlobalEventManager_onCharacterDeathGlobal;
        }

        private void GlobalEventManager_onServerDamageDealt(DamageReport report)
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || report.victimBody == null || !report.victimBody.isPlayerControlled
                || report.victimMaster == null || report.attackerMaster == null || report.attackerBody == null || report.attackerBody.inventory == null
                || report.attackerTeamIndex == TeamIndex.Player || report.attackerTeamIndex == report.victimTeamIndex || report.damageDealt <= 0f)
            {
                return;
            }
            var victim = report.victimMaster;
            if (nextStealTimes.TryGetValue(report.attackerMaster, out var nextStealTime) && Time.fixedTime < nextStealTime)
            {
                return;
            }
            var candidates = victim.inventory.itemAcquisitionOrder.Where(itemIndex => stealableItems.Contains(itemIndex) && victim.inventory.GetItemCountEffective(itemIndex) > 0).ToList();
            if (candidates.Count == 0)
            {
                return;
            }
            nextStealTimes[report.attackerMaster] = Time.fixedTime + Mathf.Max(0f, StealCooldown.Value);
            var itemIndex = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
            // a negative channeled stack disables one of the player's items
            victim.inventory.GiveItemChanneled(itemIndex, -1);
            EventLoot.GetInventory(this, report.attackerBody).GiveTemporaryItem(itemIndex);
            ItemOrb.Fly(report.victimBody.corePosition, report.attackerBody.gameObject, itemIndex);
            if (!loot.TryGetValue(report.attackerMaster, out var stolen))
            {
                stolen = new List<(CharacterMaster victim, ItemIndex itemIndex)>();
                loot[report.attackerMaster] = stolen;
            }
            stolen.Add((victim, itemIndex));
        }

        // the thief's loot flies back to the players it stole from
        private void GlobalEventManager_onCharacterDeathGlobal(DamageReport report)
        {
            if (!NetworkServer.active || report.victimMaster == null || !loot.TryGetValue(report.victimMaster, out var stolen))
            {
                return;
            }
            loot.Remove(report.victimMaster);
            var origin = report.victimBody != null ? report.victimBody.corePosition : (Vector3?)null;
            foreach (var (victim, itemIndex) in stolen)
            {
                GiveBack(victim, itemIndex, origin, silently: false);
            }
        }

        private static void GiveBack(CharacterMaster victim, ItemIndex itemIndex, Vector3? origin, bool silently)
        {
            if (victim == null || victim.inventory == null)
            {
                return;
            }
            victim.inventory.GiveItemChanneled(itemIndex, 1);
            var body = victim.GetBody();
            if (origin.HasValue && body != null && silently)
            {
                ItemOrb.FlySilently(origin.Value, body.gameObject, itemIndex);
            }
            else if (origin.HasValue && body != null)
            {
                ItemOrb.Fly(origin.Value, body.gameObject, itemIndex);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            loot.Clear();
            nextStealTimes.Clear();
            // only items that are fine for monsters to have
            stealableItems = new HashSet<ItemIndex>(SafeItems.Get());
        }

        // what the surviving thieves still have goes back too, silently as many items may fly at once
        public override void Stop()
        {
            foreach (var (thief, stolen) in loot)
            {
                var thiefBody = thief != null ? thief.GetBody() : null;
                foreach (var (victim, itemIndex) in stolen)
                {
                    GiveBack(victim, itemIndex, thiefBody != null ? thiefBody.corePosition : (Vector3?)null, silently: true);
                }
            }
            loot.Clear();
            nextStealTimes.Clear();
            EventLoot.Clear(this);
        }
    }
}
