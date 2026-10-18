using HG;
using RoR2;
using UnityEngine;

namespace RandomEvents
{
    // Makes a monster attack less often, with lower attack speed and longer skill cooldowns, and move slower. Its AI runs
    // on the host, so the host's stats are the ones that count.
    public class Slowdown : MonoBehaviour
    {
        public float attackFactor = 1f;
        public float moveFactor = 1f;

        public static void Hook()
        {
            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
        }

        public static void Apply(CharacterBody body, float attackFactor, float moveFactor = 1f)
        {
            var slowdown = body.gameObject.EnsureComponent<Slowdown>();
            slowdown.attackFactor = attackFactor;
            slowdown.moveFactor = moveFactor;
            body.MarkAllStatsDirty();
        }

        // after the stats are recalculated, which sets these anew each time
        private static void CharacterBody_RecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);
            if (!self.TryGetComponent(out Slowdown slowdown))
            {
                return;
            }
            if (slowdown.moveFactor > 1f)
            {
                self.moveSpeed /= slowdown.moveFactor;
            }
            if (slowdown.attackFactor <= 1f)
            {
                return;
            }
            self.attackSpeed /= slowdown.attackFactor;
            var skillLocator = self.skillLocator;
            if (skillLocator != null)
            {
                foreach (var skill in new[] { skillLocator.primary, skillLocator.secondaryBonusStockSkill, skillLocator.utilityBonusStockSkill, skillLocator.specialBonusStockSkill })
                {
                    if (skill != null)
                    {
                        skill.cooldownScale *= slowdown.attackFactor;
                    }
                }
            }
        }
    }
}
