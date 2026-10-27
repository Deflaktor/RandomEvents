using BepInEx;
using BepInEx.Configuration;
using RoR2;
using RoR2.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    // What an event does. An event rules out the events that have one of its ExcludedTags
    [Flags]
    public enum EventTags
    {
        None = 0,
        // about the monsters alone, e.g. which spawn or what they do
        Monsters = 1 << 0,
        // changes which of the stage's monsters spawn
        MonsterPool = 1 << 1,
        // picks its own kinds of monsters, e.g. only weak ones
        MonsterKinds = 1 << 2,
        // brings more monsters along with the ones that spawn
        ExtraMonsters = 1 << 3,
        MonsterSize = 1 << 4,
        MonsterAttackSpeed = 1 << 5,
        // changes how monsters take damage
        MonsterDamageTaken = 1 << 6,
        // changes the monsters' health or barrier
        MonsterHealth = 1 << 7,
        // relies on items it gives, which Cursed Spoils' hexes would disable
        EventItems = 1 << 8,
        // gives or takes away the players' equipment
        Equipment = 1 << 9,
        // swaps the players' equipment on every use, like the Artifact of Enigma
        EquipmentSwaps = 1 << 10,
        // makes the players lose health
        PlayerHealth = 1 << 11,
        // swaps or shares the players' items
        PlayerItems = 1 << 12,
        // changes the players' bodies
        PlayerBody = 1 << 13,
        // changes the players' skills
        PlayerSkills = 1 << 14,
        // marks players, e.g. with Essence of Heresy's marks or the soul brand of Caustic Pheromones
        PlayerMarker = 1 << 15,
        Revives = 1 << 16,
        // changes how players get hurt, or lets them hurt each other
        PlayerDamage = 1 << 17,
        // hides the players
        HiddenPlayers = 1 << 18,
        // moves the players or punishes them for moving
        PlayerMovement = 1 << 19,
        // decides whom the monsters attack
        MonsterTargets = 1 << 20,
        // picks the equipment the players get, which Equipment-exclusive combat hands out instead of its own
        EquipmentPick = 1 << 21,
    }

    public abstract class AbstractEvent
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<float> Weight;
        // its number in the list of all events, for the console commands
        public int Id;
        // see HereticTransformation
        public virtual bool SuppressesHereticTransformation => false;
        // what the event is about, which other events can rule out
        public virtual EventTags Tags => EventTags.None;
        // the tags of the events it can't happen together with
        public virtual EventTags ExcludedTags => EventTags.None;
        // whether the event can happen together with the other one; the events check each other
        public bool IsCompatibleWith(AbstractEvent other)
        {
            return (ExcludedTags & other.Tags) == 0;
        }
        protected virtual float DefaultWeight => 1f;
        public abstract string GetEventConfigName();
        public abstract string GetAnnouncement();
        public void SetupConfig(ConfigFile config, List<AbstractEvent> allEvents)
        {
            var conditionDescription = "";
            var condition = GetFullConditionDescription(allEvents);
            if (!condition.IsNullOrWhiteSpace())
                conditionDescription += "\nCondition: " + condition;
            Enabled = config.Bind(GetEventConfigName(), "SectionEnabled", true, $"Enable {GetEventConfigName()}: {GetDescription()}{conditionDescription}");
            Weight = config.Bind(GetEventConfigName(), "Weight", DefaultWeight, $"Weight for the event.");
            AddConfig(config);
        }
        protected abstract void AddConfig(ConfigFile config);
        public float GetWeight()
        {
            return Weight.Value;
        }
        public bool IsEnabled()
        {
            return Enabled.Value;
        }
        public bool IsActive()
        {
            return RandomEvents.instance.m_activeEvents.Contains(this);
        }

        /// <summary>
        /// Whether this Event should be hooked and setup or not. This is only checked at game startup.
        /// </summary>
        /// <returns></returns>
        public virtual bool LoadCondition()
        {
            return true;
        }
        public abstract bool Condition(List<AbstractEvent> activeOtherEvents);
        public abstract void Preload();
        public abstract void Prepare();
        public abstract void Start(List<AbstractEvent> activeOtherEvents);
        public abstract void Stop();
        public abstract void Hook();
        public abstract string GetDescription();
        // the text players see in the event's popup
        public virtual string GetPopupDescription()
        {
            return GetDescription();
        }
        public abstract string GetConditionDescription();

        // its own conditions and the events it can't happen together with
        public string GetFullConditionDescription(List<AbstractEvent> allEvents)
        {
            var parts = new List<string>();
            var own = GetConditionDescription();
            if (!own.IsNullOrWhiteSpace())
                parts.Add(own.TrimEnd('.'));
            bool noMonsterEvents = (ExcludedTags & EventTags.Monsters) != 0;
            if (noMonsterEvents)
                parts.Add("no event about monsters alone");
            var names = allEvents
                .Where(other => other != this && !(IsCompatibleWith(other) && other.IsCompatibleWith(this)))
                .Where(other => !noMonsterEvents || (other.Tags & EventTags.Monsters) == 0)
                .Select(other => $"\"{other.GetEventConfigName()}\"")
                .ToList();
            if (names.Count > 0)
                parts.Add((names.Count == 1 ? "event " : "events ") + JoinWithAnd(names) + " inactive");
            if (parts.Count == 0)
                return "";
            // the last "and" belongs before the last part
            var lastAnd = parts[0].LastIndexOf(", and ");
            if (parts.Count > 1 && !own.IsNullOrWhiteSpace() && lastAnd >= 0)
                parts[0] = parts[0].Remove(lastAnd, ", and ".Length).Insert(lastAnd, ", ");
            var text = string.Join(", ", parts.Take(parts.Count - 1)) + (parts.Count > 1 ? ", and " : "") + parts.Last() + ".";
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private static string JoinWithAnd(List<string> words)
        {
            return words.Count == 1 ? words[0] : string.Join(", ", words.Take(words.Count - 1)) + " and " + words.Last();
        }
    }
}
