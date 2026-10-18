using HG;
using RoR2;
using UnityEngine;

namespace RandomEvents
{
    // Makes a monster attack less often: lower attack speed and longer skill cooldowns. Its AI runs on the host, so the
    // host's stats are the ones that count.
    public class SlowAttacks : MonoBehaviour
    {
        public float factor = 1f;

        public static void Hook()
        {
            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
        }

        public static void Apply(CharacterBody body, float factor)
        {
            body.gameObject.EnsureComponent<SlowAttacks>().factor = factor;
            body.MarkAllStatsDirty();
        }

        // after the stats are recalculated, which sets these anew each time
        private static void CharacterBody_RecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);
            if (!self.TryGetComponent(out SlowAttacks slowAttacks) || slowAttacks.factor <= 1f)
            {
                return;
            }
            self.attackSpeed /= slowAttacks.factor;
            var skillLocator = self.skillLocator;
            if (skillLocator != null)
            {
                foreach (var skill in new[] { skillLocator.primary, skillLocator.secondaryBonusStockSkill, skillLocator.utilityBonusStockSkill, skillLocator.specialBonusStockSkill })
                {
                    if (skill != null)
                    {
                        skill.cooldownScale *= slowAttacks.factor;
                    }
                }
            }
        }
    }
}
