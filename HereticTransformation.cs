using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Linq;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Events that change players' items or equipment for a while skip the Heretic transformation while they run.
    // Since 1.5.0 it also makes the Heretic the player's original body, so it would outlast the event.
    public static class HereticTransformation
    {
        public static void Hook()
        {
            IL.RoR2.CharacterMaster.OnInventoryChanged += CharacterMaster_OnInventoryChanged;
        }

        private static void CharacterMaster_OnInventoryChanged(ILContext il)
        {
            // skip the whole Heretic block, not just TransformBody, since it also changes the original body and body handling before that call
            ILCursor c = new ILCursor(il);
            var skipHeretic = c.DefineLabel();
            c.GotoNext(
            x => x.MatchLdstr("HereticBody"),
            x => x.MatchCall(typeof(BodyCatalog), nameof(BodyCatalog.FindBodyPrefab))
            );
            var end = c.Clone();
            end.GotoNext(
            x => x.MatchLdarg(0),
            x => x.MatchCall<CharacterMaster>("UpdateMoneyBasedItems")
            );
            end.MarkLabel(skipHeretic);
            c.EmitDelegate<Func<bool>>(IsSuppressed);
            c.Emit(OpCodes.Brtrue, skipHeretic);
        }

        private static bool IsSuppressed()
        {
            return ModConfig.Enabled.Value && NetworkServer.active && RandomEvents.instance.m_activeEvents.Any(ev => ev.SuppressesHereticTransformation);
        }
    }
}
