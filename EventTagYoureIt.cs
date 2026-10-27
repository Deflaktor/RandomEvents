using RoR2;
using System.Collections.Generic;

namespace RandomEvents
{
    public class EventTagYoureIt : TagEvent
    {
        public override string GetEventConfigName()
        {
            return "TagYoureIt";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_TAG_YOURE_IT");
        }
        public override string GetDescription()
        {
            return "One player is it: all monsters hunt them, and they and their drones and other allies deal no damage. Everyone else is invincible and deals a bit more damage, so that the team deals as much as usual. Whoever is it passes it on by touching another player.";
        }
        public override string GetPopupDescription()
        {
            return "Whoever is it gets hunted by every monster and can't hurt them. Touch another player to pass it on!";
        }

        protected override string TaggedMessageToken => "EVENT_TAG_YOURE_IT_TAGGED";

        protected override bool IsInvincible(CharacterBody player)
        {
            return player != tagged;
        }

        // the others make up for what is missing, e.g. 1.5 times their damage when 2 of 3 players fight
        protected override float DamageFactor(CharacterBody player, int playerCount)
        {
            return player != tagged && playerCount > 1 ? playerCount / (playerCount - 1f) : 0f;
        }

        protected override CharacterBody RedirectMonster(CharacterBody current, List<CharacterBody> players)
        {
            return tagged;
        }
    }
}
