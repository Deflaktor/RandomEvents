using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Linq;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Events that change players' items or equipment for a while skip the Heretic transformation while they run, and
    // so does a player who holds one of the heresy items only temporarily, e.g. SkillsOnly's Visions of Heresy. Since
    // 1.5.0 the transformation also makes the Heretic the player's original body, so it would outlast the event.
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
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<CharacterMaster, bool>>(IsSuppressed);
            c.Emit(OpCodes.Brtrue, skipHeretic);
        }

        private static readonly ItemDef[] HeresyItems =
        {
            RoR2Content.Items.LunarPrimaryReplacement, RoR2Content.Items.LunarSecondaryReplacement,
            RoR2Content.Items.LunarUtilityReplacement, RoR2Content.Items.LunarSpecialReplacement
        };

        private static bool IsSuppressed(CharacterMaster master)
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active)
            {
                return false;
            }
            if (RandomEvents.instance.m_activeEvents.Any(ev => ev.SuppressesHereticTransformation))
            {
                return true;
            }
            // the events' temporary items are channeled stacks, which also remain after the event ended until removed
            var inventory = master.inventory;
            return inventory != null && HeresyItems.Any(itemDef => inventory.GetItemCountChanneled(itemDef) > 0
                && inventory.GetItemCountPermanent(itemDef) + inventory.GetItemCountTemp(itemDef.itemIndex) <= 0);
        }
    }
}
