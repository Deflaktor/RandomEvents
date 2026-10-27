using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RandomEvents
{
    public class EventTagKing : TagEvent
    {
        private ConfigEntry<float> DamageMultiplierPerPlayer;

        public override string GetEventConfigName()
        {
            return "TagKing";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_TAG_KING");
        }
        public override string GetDescription()
        {
            return "One player is king: they are invincible, and only they and their drones and other allies deal damage, as much as all players together. Monsters ignore the king and attack the others. Whoever touches the king becomes king.";
        }
        public override string GetPopupDescription()
        {
            return "The king is invincible and deals extra damage, and the monsters ignore them. Touch the king to take the crown!";
        }
        protected override void AddConfig(ConfigFile config)
        {
            base.AddConfig(config);
            DamageMultiplierPerPlayer = config.Bind(GetEventConfigName(), "Damage Multiplier Per Player", 1f, "The king's damage is multiplied by this times the number of living players.");
        }

        protected override string TaggedMessageToken => "EVENT_TAG_KING_CROWNED";

        protected override bool IsInvincible(CharacterBody player)
        {
            return player == tagged;
        }

        protected override float DamageFactor(CharacterBody player, int playerCount)
        {
            return player == tagged ? Mathf.Max(0f, DamageMultiplierPerPlayer.Value) * playerCount : 0f;
        }

        // the nearest player but the king, if there is one
        protected override CharacterBody RedirectMonster(CharacterBody current, List<CharacterBody> players)
        {
            return current == tagged ? players.FirstOrDefault(player => player != tagged) : null;
        }
    }
}
