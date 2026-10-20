using RoR2;
using UnityEngine.Networking;

namespace RandomEvents
{
    // One of ExperimentalEquipment's equipment and what it does instead. The event picks one for each time it happens.
    public abstract class ExperimentalEquipment
    {
        public EventExperimentalEquipment Event { get; set; }
        public abstract EquipmentDef EquipmentDef { get; }
        // of its normal cooldown
        public virtual float CooldownScale => 0.5f;
        // for the event's description, e.g. "Royal Capacitor inflates a monster until it explodes"
        public abstract string Description { get; }
        public abstract string PopupDescription { get; }

        public virtual void Preload()
        {
        }

        public virtual void Hook()
        {
        }

        // whether using it does what Fire does instead of its normal action
        public virtual bool ReplacesAction => true;

        // On the host when a player uses it; returning false fires nothing and keeps the charge
        public virtual bool Fire(EquipmentSlot slot)
        {
            return false;
        }

        // on the host, for each player who has it
        public virtual void FixedUpdatePlayer(CharacterBody body)
        {
        }

        public virtual void Stop()
        {
        }

        // whether this is the event's equipment right now
        protected bool IsActive()
        {
            return ModConfig.Enabled.Value && NetworkServer.active && Event != null && Event.IsActive() && Event.Current == this;
        }

        protected bool IsExperimental(CharacterBody body)
        {
            return IsActive() && body != null && body.GetComponent<EventExperimentalEquipment.ExperimentalComponent>() != null;
        }
    }
}
