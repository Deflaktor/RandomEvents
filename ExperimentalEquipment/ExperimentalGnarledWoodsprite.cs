using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RandomEvents
{
    // The Gnarled Woodsprite works as usual, but the other player it is sent to has no cooldowns and Spinel Tonic's boost,
    // without the Tonic Affliction it would leave. Only with more than one player.
    public class ExperimentalGnarledWoodsprite : ExperimentalEquipment
    {
        // buffs that every player's game works the stats and skills out from
        private static readonly BuffDef[] Boosts = { RoR2Content.Buffs.NoCooldowns, RoR2Content.Buffs.TonicBuff };
        private readonly BuffTracker buffs = new BuffTracker();
        // the player each one's woodsprite is with, and the woodsprite
        private readonly Dictionary<CharacterBody, CharacterBody> boosted = new Dictionary<CharacterBody, CharacterBody>();
        private readonly Dictionary<CharacterBody, HealingFollowerController> followers = new Dictionary<CharacterBody, HealingFollowerController>();

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.PassiveHealing;
        public override string Description => "Gnarled Woodsprite gives the other player it is sent to no cooldowns and Spinel Tonic's boost without its downside";
        public override string PopupDescription => "Send it to another player: while it stays with them, they have no cooldowns and Spinel Tonic's boost without the downside.";
        public override bool IsAvailable => PlayerCharacterMasterController.instances.Count(player => player.isConnected) > 1;

        // it is sent and heals as usual
        public override bool ReplacesAction => false;

        public override void FixedUpdatePlayer(CharacterBody body)
        {
            // those whose woodsprite's owner is gone, e.g. dead, lose the boost too
            foreach (var owner in boosted.Keys.Where(owner => owner == null || !owner.healthComponent.alive).ToList())
            {
                foreach (var boost in Boosts)
                {
                    buffs.Remove(boosted[owner], boost);
                }
                boosted.Remove(owner);
            }
            var target = Target(body);
            boosted.TryGetValue(body, out var current);
            if (current == target)
            {
                return;
            }
            if (current != null)
            {
                foreach (var boost in Boosts)
                {
                    buffs.Remove(current, boost);
                }
            }
            if (target != null)
            {
                foreach (var boost in Boosts)
                {
                    buffs.Add(target, boost);
                }
                boosted[body] = target;
            }
            else
            {
                boosted.Remove(body);
            }
        }

        // the other living player its woodsprite is with, if any
        private CharacterBody Target(CharacterBody owner)
        {
            if (!followers.TryGetValue(owner, out var follower) || follower == null)
            {
                follower = Object.FindObjectsOfType<HealingFollowerController>().FirstOrDefault(candidate => candidate.ownerBodyObject == owner.gameObject);
                if (follower == null)
                {
                    return null;
                }
                followers[owner] = follower;
            }
            var target = follower.targetBodyObject != null ? follower.targetBodyObject.GetComponent<CharacterBody>() : null;
            return target != null && target != owner && target.isPlayerControlled && target.healthComponent != null && target.healthComponent.alive ? target : null;
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            boosted.Clear();
            followers.Clear();
        }
    }
}
