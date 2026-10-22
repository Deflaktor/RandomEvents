using EntityStates;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RandomEvents
{
    // What the events need to know about a monster that its prefab doesn't say outright: whether its death leaves a
    // corpse, and whether its primary skill fires projectiles. Both come from the code of its entity states, so they are
    // kept here.
    public static class MonsterTraits
    {
        // The death states that remove the monster's model or body themselves, e.g. bursting, imploding or dissolving,
        // rather than leaving it lying for the game to clear, as GenericCharacterDeath does. Found in the game's code for
        // version 1.5.0: those that destroy the model or the body, or don't let the game do it.
        private static readonly HashSet<string> CorpselessDeathStates = new HashSet<string>
        {
            "EntityStates.AcidLarva.Death",
            "EntityStates.Acolyte.DeathState",
            "EntityStates.AffixEarthHealer.DeathState",
            "EntityStates.Assassin2.DeathState",
            "EntityStates.BeetleQueenMonster.BeetleWardDeath",
            "EntityStates.Bell.DeathState",
            "EntityStates.BigMush.DeathState",
            "EntityStates.BrotherMonster.BrotherPhantom.DeathState",
            "EntityStates.BrotherMonster.InstantDeathState",
            "EntityStates.BrotherMonster.KarmaSpire.DeathState",
            "EntityStates.BrotherMonster.SkyLeapDeathState",
            "EntityStates.BrotherMonster.Throne.Death",
            "EntityStates.ChampRushReaper.DeathState",
            "EntityStates.ChampRushReaper.PhaseTransitionDeathState",
            "EntityStates.ChildMonster.DeathState",
            "EntityStates.ClayBoss.DeathState",
            "EntityStates.DefectiveUnit.DeathState",
            "EntityStates.DreamDemon.Death",
            "EntityStates.Drone.DeathState",
            "EntityStates.Drone.DTDroneDeathState",
            "EntityStates.Drone.MegaDroneDeathState",
            "EntityStates.ExtractedSoul.AcolyteExtractedSoul.DeathState",
            "EntityStates.ExtractorUnit.DeathState",
            "EntityStates.FalseSonBoss.BrokenCrystalDeathState",
            "EntityStates.FalseSonBoss.CrystalDeathState",
            "EntityStates.FalseSonBoss.SkyJumpDeathState",
            "EntityStates.Fauna.BirdsharkDeathState",
            "EntityStates.FlyingVermin.FallingDeath",
            "EntityStates.GreaterWispMonster.DeathState",
            "EntityStates.Gup.BaseSplitDeath",
            "EntityStates.Hollowman.DeathState",
            "EntityStates.ImpBossMonster.DeathState",
            "EntityStates.ImpMonster.DeathState",
            "EntityStates.IronHauler.DeathState",
            "EntityStates.JellyfishMonster.DeathState",
            "EntityStates.JellyfishMonster.JellyNova",
            "EntityStates.JunkCube.DeathState",
            "EntityStates.LilReaper.DeathState",
            "EntityStates.LunarExploderMonster.DeathState",
            "EntityStates.LunarWisp.DeathState",
            "EntityStates.LynxArcher.ShadowClone.DeathState",
            "EntityStates.MagmaWorm.DeathState",
            "EntityStates.MajorConstruct.Death",
            "EntityStates.MinePod.DeathState",
            "EntityStates.MinorConstruct.DeathState",
            "EntityStates.NullifierMonster.DeathState",
            "EntityStates.ParentMonster.DeathState",
            "EntityStates.Providence.DeathState",
            "EntityStates.ProvidenceShadowClone.ProvidenceShadowCloneDeath",
            "EntityStates.Reaper.DeathState",
            "EntityStates.RoboBallBoss.DeathState",
            "EntityStates.ScavMonster.Death",
            "EntityStates.Scorchling.DeathState",
            "EntityStates.SolusAmalgamator.DeathState",
            "EntityStates.SolusMine.Detonate",
            "EntityStates.SolusWing.DeathState",
            "EntityStates.SpiritMonster.DeathState",
            "EntityStates.Tanker.DeathState",
            "EntityStates.Tanker.GreasePuddle.GreasePuddleDeath",
            "EntityStates.Thruster.DeathState",
            "EntityStates.TitanMonster.DeathState",
            "EntityStates.Totem.DeathState",
            "EntityStates.Totem.Pillar.DeathState",
            "EntityStates.VagrantMonster.DeathState",
            "EntityStates.VoidBarnacle.DeathState",
            "EntityStates.VoidInfestor.Death",
            "EntityStates.VoidJailer.DeathState",
            "EntityStates.VoidMegaCrab.DeathState",
            "EntityStates.VoidRaidCrab.DeathState",
            "EntityStates.VoidRaidCrab.EscapeDeath",
            "EntityStates.VultureHunter.DeathExplodeAirState",
            "EntityStates.Wake.Death",
            "EntityStates.Wisp1Monster.DeathState",
            "EntityStates.WorkerUnit.DeathState",
        };

        private static readonly Dictionary<GameObject, bool> leavesCorpse = new Dictionary<GameObject, bool>();
        private static readonly Dictionary<GameObject, bool> firesProjectiles = new Dictionary<GameObject, bool>();

        public static CharacterBody GetBody(DirectorCard card)
        {
            var master = card != null && card.spawnCard != null && card.spawnCard.prefab != null ? card.spawnCard.prefab.GetComponent<CharacterMaster>() : null;
            return master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
        }

        // Whether its body is left lying where it died: its death state is the game's usual one, or one built on it
        // that doesn't remove the body itself
        public static bool LeavesCorpse(DirectorCard card)
        {
            var body = GetBody(card);
            if (body == null)
            {
                return false;
            }
            if (!leavesCorpse.TryGetValue(body.gameObject, out var result))
            {
                var deathState = body.TryGetComponent(out CharacterDeathBehavior death) ? death.deathState.stateType : null;
                result = deathState != null && typeof(GenericCharacterDeath).IsAssignableFrom(deathState) && !CorpselessDeathStates.Contains(deathState.FullName);
                leavesCorpse[body.gameObject] = result;
            }
            return result;
        }

        // Whether the default skill of its primary fires projectiles. A state that fires one has a projectile prefab to
        // fire, or is the game's generic projectile state; a state that charges up first, e.g. ChargeFireball, fires
        // through its Fire counterpart, e.g. FireFireball.
        public static bool PrimaryFiresProjectiles(DirectorCard card)
        {
            var body = GetBody(card);
            if (body == null)
            {
                return false;
            }
            if (!firesProjectiles.TryGetValue(body.gameObject, out var result))
            {
                var primary = body.TryGetComponent(out SkillLocator skills) ? skills.primary : null;
                var skillDef = primary != null && primary.skillFamily != null && primary.skillFamily.variants.Length > 0 ? primary.skillFamily.defaultSkillDef : null;
                result = skillDef != null && FiresProjectiles(skillDef.activationState.stateType);
                firesProjectiles[body.gameObject] = result;
            }
            return result;
        }

        private static bool FiresProjectiles(Type state)
        {
            if (state == null)
            {
                return false;
            }
            if (typeof(GenericProjectileBaseState).IsAssignableFrom(state) || HasProjectileField(state))
            {
                return true;
            }
            if (state.Name.StartsWith("Charge"))
            {
                var fire = state.Assembly.GetType(state.Namespace + ".Fire" + state.Name.Substring("Charge".Length));
                return fire != null && fire != state && (typeof(GenericProjectileBaseState).IsAssignableFrom(fire) || HasProjectileField(fire));
            }
            return false;
        }

        private static bool HasProjectileField(Type state)
        {
            for (var type = state; type != null && type != typeof(EntityState); type = type.BaseType)
            {
                if (type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Any(field => field.FieldType == typeof(GameObject) && field.Name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }
            return false;
        }

        // the share of the pool's spawn weight whose monsters pass the test
        public static float WeightShare(IEnumerable<WeightedSelection<DirectorCard>.ChoiceInfo> choices, Func<DirectorCard, bool> test)
        {
            var list = choices.Where(choice => choice.value != null && choice.weight > 0f).ToList();
            var total = list.Sum(choice => choice.weight);
            return total > 0f ? list.Where(choice => test(choice.value)).Sum(choice => choice.weight) / total : 0f;
        }
    }
}
