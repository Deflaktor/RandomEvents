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
    public abstract class AbstractEvent
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<float> Weight;
        public abstract string GetEventConfigName();
        public abstract string GetAnnouncement();
        public void SetupConfig(ConfigFile config)
        {
            var conditionDescription = "";
            if (GetConditionDescription().IsNullOrWhiteSpace())
                conditionDescription += "\nCondition: " + GetConditionDescription();
            Enabled = config.Bind(GetEventConfigName(), "SectionEnabled", true, $"Enable {GetEventConfigName()}: {GetDescription()}{conditionDescription}");
            Weight = config.Bind(GetEventConfigName(), "Weight", 1.0f, $"Weight for the event.");
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
        public abstract string GetConditionDescription();
    }
}
