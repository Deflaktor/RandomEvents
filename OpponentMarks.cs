using Mono.Cecil.Cil;
using MonoMod.Cil;
using PerClientSync;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Marks each player's opponents with the soul brand of Caustic Pheromones, except while they are cloaked. Every client gets the brand's buff in the
    // buff lists of its player's opponents only, where it does nothing but show. The host never has the buff, so none of
    // its effects happen, e.g. friendly fire on the branded or monsters drawn to them; the host's own player sees the brand
    // through the game's check for showing it.
    public static class OpponentMarks
    {
        // CharacterBody's dirty bit for its buffs
        private const uint BuffsBit = 2u;

        // whether the player of the first master fights the body
        private static Func<CharacterMaster, CharacterBody, bool> isOpponent;
        private static readonly List<MarkView> views = new List<MarkView>();

        public static void Hook()
        {
            IL.RoR2.CharacterBody.UpdateAllTemporaryVisualEffects += CharacterBody_UpdateAllTemporaryVisualEffects;
        }

        // Shows each player the players' bodies they fight, until Hide
        public static void Show(Func<CharacterMaster, CharacterBody, bool> isOpponent)
        {
            OpponentMarks.isOpponent = isOpponent;
            Refresh();
        }

        // Marks new bodies, e.g. revived players', and sends every client its marks again, e.g. after the teams changed
        public static void Refresh()
        {
            if (isOpponent == null || !NetworkServer.active)
            {
                return;
            }
            views.RemoveAll(view => !view);
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body.master && body.master.playerCharacterMasterController && !body.GetComponent<MarkView>())
                {
                    views.Add(body.gameObject.AddComponent<MarkView>());
                }
            }
            foreach (var view in views)
            {
                view.SetDirtyForAll(BuffsBit);
                // the host's own view
                view.Component.MarkAllStatsDirty();
            }
        }

        public static void Hide()
        {
            if (isOpponent == null)
            {
                return;
            }
            isOpponent = null;
            foreach (var view in views)
            {
                if (!view)
                {
                    continue;
                }
                // clients get the real buffs back with the next update, before the view goes
                view.SetDirtyForAll(BuffsBit);
                view.Component.MarkAllStatsDirty();
                UnityEngine.Object.Destroy(view, 1f);
            }
            views.Clear();
        }

        private static void CharacterBody_UpdateAllTemporaryVisualEffects(ILContext il)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(MoveType.After,
                x => x.MatchLdsfld(typeof(DLC4Content.Buffs), nameof(DLC4Content.Buffs.MalignantAttractionActive)),
                x => x.MatchCallOrCallvirt<CharacterBody>(nameof(CharacterBody.HasBuff))))
            {
                Log.LogError("Could not find the soul brand in CharacterBody.UpdateAllTemporaryVisualEffects, so the host's player sees no opponent marks.");
                return;
            }
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<bool, CharacterBody, bool>>((branded, body) => branded || IsMarkedForHost(body));
        }

        private static bool IsMarkedForHost(CharacterBody body)
        {
            if (isOpponent == null || !NetworkServer.active)
            {
                return false;
            }
            var host = LocalUserManager.GetFirstLocalUser()?.cachedMasterController;
            return host && host.master && !body.hasCloakBuff && isOpponent(host.master, body);
        }

        // A player's body as each client shows it: branded for the players who fight it
        private class MarkView : ClientView<CharacterBody>
        {
            private bool inserted;
            private int insertedAt;
            private int realCount;

            protected override void Apply(NetworkConnection client)
            {
                inserted = false;
                var body = Component;
                var brand = DLC4Content.Buffs.MalignantAttractionActive;
                var viewer = PerClient.GetPlayer(client);
                // cloaking changes the body's buffs, so it is sent again with the brand gone
                if (isOpponent == null || !brand || !viewer || !viewer.master || body.HasBuff(brand) || body.hasCloakBuff || !isOpponent(viewer.master, body))
                {
                    return;
                }
                var index = brand.buffIndex;
                // clients read the buffs in the order of their indices
                int position = 0;
                while (position < body.activeBuffsListCount && body.activeBuffsList[position] < index)
                {
                    position++;
                }
                realCount = body.buffs[(int)index];
                HG.ArrayUtils.ArrayInsert(ref body.activeBuffsList, ref body.activeBuffsListCount, position, in index);
                insertedAt = position;
                inserted = true;
                body.buffs[(int)index] = 1;
            }

            protected override void Restore()
            {
                if (!inserted)
                {
                    return;
                }
                var body = Component;
                HG.ArrayUtils.ArrayRemoveAt(body.activeBuffsList, ref body.activeBuffsListCount, insertedAt);
                body.buffs[(int)DLC4Content.Buffs.MalignantAttractionActive.buffIndex] = realCount;
                inserted = false;
            }
        }
    }
}
