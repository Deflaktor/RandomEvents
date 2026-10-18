using HG;
using RoR2;
using UnityEngine;

namespace RandomEvents
{
    // Multiplies how often a monster attacks, through its attack speed and skill cooldowns, and how fast it moves. Its AI
    // runs on the host, so the host's stats are the ones that count.
    public class Slowdown : MonoBehaviour
    {
        public float attackSpeedMultiplier = 1f;
        public float moveSpeedMultiplier = 1f;

        public static void Hook()
        {
            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
        }

        public static void Apply(CharacterBody body, float attackSpeedMultiplier, float moveSpeedMultiplier = 1f)
        {
            var slowdown = body.gameObject.EnsureComponent<Slowdown>();
            slowdown.attackSpeedMultiplier = attackSpeedMultiplier;
            slowdown.moveSpeedMultiplier = moveSpeedMultiplier;
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
            if (slowdown.moveSpeedMultiplier > 0f && slowdown.moveSpeedMultiplier != 1f)
            {
                self.moveSpeed *= slowdown.moveSpeedMultiplier;
            }
            if (slowdown.attackSpeedMultiplier <= 0f || slowdown.attackSpeedMultiplier == 1f)
            {
                return;
            }
            self.attackSpeed *= slowdown.attackSpeedMultiplier;
            var skillLocator = self.skillLocator;
            if (skillLocator != null)
            {
                foreach (var skill in new[] { skillLocator.primary, skillLocator.secondaryBonusStockSkill, skillLocator.utilityBonusStockSkill, skillLocator.specialBonusStockSkill })
                {
                    if (skill != null)
                    {
                        skill.cooldownScale /= slowdown.attackSpeedMultiplier;
                    }
                }
            }
        }
    }
}
