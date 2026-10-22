using MonoMod.Cil;
using RoR2;
using RoR2.CharacterAI;
using System;

namespace RandomEvents
{
    // A monster's AI picks what to do by how far its target is, measured as if the monster had its normal size: the
    // distance divided by its size, minus both radii. The game leaves the radii at their full size, so the bigger the
    // monster, the further the distance goes below zero, until none of its skill drivers fits and it stands idle, e.g.
    // GiantBalloons' inflated monsters. Divided by its size as well, the radii fit the distance again. The AI runs on
    // the host.
    public static class ScaledMonsterAi
    {
        public static void Hook()
        {
            IL.RoR2.CharacterAI.BaseAI.GameObjectPassesSkillDriverFilters += BaseAI_GameObjectPassesSkillDriverFilters;
        }

        private static void BaseAI_GameObjectPassesSkillDriverFilters(ILContext il)
        {
            var c = new ILCursor(il);
            // the monster's own radius, then its target's
            for (int i = 0; i < 2; i++)
            {
                if (!c.TryGotoNext(MoveType.After, x => x.MatchCallvirt<CharacterBody>("get_radius")))
                {
                    Log.LogError("Could not find the radii in BaseAI.GameObjectPassesSkillDriverFilters to fix the AI of scaled monsters.");
                    return;
                }
                c.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float, BaseAI, float>>(ToNormalSize);
            }
        }

        private static float ToNormalSize(float radius, BaseAI ai)
        {
            return ModConfig.Enabled.Value && ai != null && ai.body != null ? radius * ai.body.invCurrentScale : radius;
        }
    }
}
