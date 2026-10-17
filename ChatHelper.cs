using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    class ChatHelper
    {
        private static System.Random rand = new System.Random();

        private const string GrayColor = "7e91af";
        private const string RedColor = "ed4c40";
        private const string LunarColor = "5cb1ed";
        
        private static string GetPlayerColor(PlayerCharacterMasterController pc)
        {
            var userName = pc.GetDisplayName();
            var survivorDef = SurvivorCatalog.FindSurvivorDefFromBody(pc.master?.bodyPrefab);
            if (survivorDef != null && survivorDef.primaryColor != null) {
                return ColorUtility.ToHtmlStringRGB(survivorDef.primaryColor);
            }
            var body = pc.master?.GetBody();
            if (body != null && body.bodyColor != null)
            {
                return ColorUtility.ToHtmlStringRGB(body.bodyColor);
            }
            return "f27b0c";
        }

        private static string GetColoredPlayerName(PlayerCharacterMasterController playerCharacterMasterController)
        {
            var playerColor = GetPlayerColor(playerCharacterMasterController);
            var body = playerCharacterMasterController.master.GetBody();
            return $"<color=#{playerColor}>{body.GetUserName()}</color>";
        }

        // a player's name in their survivor's color, for event messages
        public static string GetColoredPlayerName(CharacterBody body)
        {
            var pc = body.master != null ? body.master.playerCharacterMasterController : null;
            if (pc == null)
            {
                return body.GetDisplayName();
            }
            return $"<color=#{GetPlayerColor(pc)}>{body.GetUserName()}</color>";
        }

        // a message of a running event, e.g. who it picked
        public static void SendEventMessage(string token, params object[] args)
        {
            Send($"<style=cEvent>{Language.GetStringFormatted(token, args)}</style>");
        }

        public static void LunarShopTerminalUsesLeft(PlayerCharacterMasterController playerCharacterMasterController, int usesLeft)
        {
            var coloredPlayerName = GetColoredPlayerName(playerCharacterMasterController);
            if(usesLeft > 1)
            {
                Send($"{coloredPlayerName} <color=#{GrayColor}>can use a shop terminal</color> <color=#{RedColor}>{usesLeft}</color> <color=#{GrayColor}>more times.</color>");
            }
            else if (usesLeft == 1)
            {
                Send($"{coloredPlayerName} <color=#{GrayColor}>can use a shop terminal</color> <color=#{RedColor}>{usesLeft}</color> <color=#{GrayColor}>more time.</color>");
            }
            else
            {
                Send($"{coloredPlayerName} <color=#{GrayColor}>can no longer use shop terminals.</color>");
            }
        }

        public static void LunarRecyclerUsesLeft(int usesLeft)
        {
            if (usesLeft > 1)
            {
                Send($"The <color=#{LunarColor}>Lunar Recycler</color> <color=#{GrayColor}>can be used</color> <color=#{RedColor}>{usesLeft}</color> <color=#{GrayColor}>more times.</color>");
            }
            else if (usesLeft == 1)
            {
                Send($"The <color=#{LunarColor}>Lunar Recycler</color> <color=#{GrayColor}>can be used</color> <color=#{RedColor}>{usesLeft}</color> <color=#{GrayColor}>more time.</color>");
            }
            else
            {
                Send($"The <color=#{LunarColor}>Lunar Recycler</color> <color=#{GrayColor}>can no longer be used.</color>");
            }
        }

        public static string GetItemNames(PickupIndex[] pickupIndexes)
        {
            var counts = pickupIndexes.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

            var itemNames = new List<string>();
            foreach (var entry in counts)
            {
                var pickupIndex = entry.Key;
                var count = entry.Value;
                var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
                var itemName = pickupDef.internalName;
                ColorCatalog.ColorIndex colorIndex = ColorCatalog.ColorIndex.Error;
                if (pickupDef.itemIndex != ItemIndex.None)
                {
                    var itemDef = ItemCatalog.GetItemDef(pickupDef.itemIndex);
                    itemName = Language.GetString(itemDef.nameToken);
                    colorIndex = ItemTierCatalog.GetItemTierDef(itemDef.tier).colorIndex;
                }
                else if (pickupDef.equipmentIndex != EquipmentIndex.None)
                {
                    var equipmentDef = EquipmentCatalog.GetEquipmentDef(pickupDef.equipmentIndex);
                    itemName = Language.GetString(equipmentDef.nameToken);
                    colorIndex = equipmentDef.colorIndex;
                }
                var colorHexString = ColorCatalog.GetColorHexString(colorIndex);
                if (count > 1)
                {
                    itemNames.Add($"<color=#{colorHexString}>{itemName}</color> x {count}");
                }
                else
                {
                    itemNames.Add($"<color=#{colorHexString}>{itemName}</color>");
                }
            }

            return string.Join(", ", itemNames);
        }

        public static void AnnounceEvent(string sound)
        {
            int length = 10;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_EVENT_{r}");
            SendWithSound($"<size=18px><style=cEvent>{eventText}</style></size>", sound);
        }

        public static void AnnounceDoubleEvent(string sound)
        {
            int length = 5;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_DOUBLE_EVENT_{r}");
            SendWithSound($"<size=22px><style=cEvent>{eventText}</style></size>", sound);
        }
        public static void AnnounceTripleEvent(string sound)
        {
            int length = 3;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_TRIPLE_EVENT_{r}");
            SendWithSound($"<size=26px><style=cEvent>{eventText}</style></size>", sound);
        }
        public static void AnnounceEventConclusion()
        {
            int length = 12;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_EVENT_CONCLUDE_{r}");
            Send($"<size=18px><style=cEvent>{eventText}</style></size>");
        }

        public static void SendToken(string token)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = Language.GetStringFormatted(token)
            });
        }

        public static void Send(string message)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = message
            });
        }

        // the game's message type for chat, see Chat.SendBroadcastChat
        private const short ChatMessageType = 59;

        // Sends a message with a sound that every player hears at their own body. The NPC chat message, which the game
        // uses for boss dialogue, plays its sound at its sender on each client, so each player gets their own copy.
        public static void SendWithSound(string message, string sound)
        {
            var livingBodies = Helper.GetLivingPlayerBodies();
            foreach (var connection in NetworkServer.connections)
            {
                if (connection == null)
                {
                    continue;
                }
                var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(networkUser => networkUser.connectionToClient == connection);
                var body = user != null && user.master != null ? user.master.GetBody() : null;
                // a dead player hears it at a living one
                if ((body == null || !body.healthComponent.alive) && livingBodies.Count > 0)
                {
                    body = livingBodies[0];
                }
                var chatMessage = new Chat.NpcChatMessage
                {
                    sender = body != null ? body.gameObject : null,
                    baseToken = message,
                    formatStringToken = "{0}",
                    sound = sound
                };
                var writer = new NetworkWriter();
                writer.StartMessage(ChatMessageType);
                writer.Write(chatMessage.GetTypeIndex());
                writer.Write(chatMessage);
                writer.FinishMessage();
                connection.SendWriter(writer, QosChannelIndex.chat.intVal);
            }
        }

        private static int popupCount;

        // Shows a popup like an item pickup's to every player, with the placeholder icon of equipment cut from the
        // game, an exclamation mark. That equipment can't be picked up, so the popup never clashes with a real
        // pickup's, which the queue skips if it names the same equipment as the popup before it. Alternating between
        // two keeps consecutive popups apart.
        public static void SendPopup(string title, string description)
        {
            var equipmentDef = popupCount++ % 2 == 0 ? JunkContent.Equipment.SoulJar : JunkContent.Equipment.OrbitalLaser;
            if (equipmentDef == null || equipmentDef.equipmentIndex == EquipmentIndex.None)
            {
                return;
            }
            var overrideInfo = new CharacterMasterNotificationQueue.CustomOverrideInfo
            {
                titleText = title,
                descriptionText = description,
                // the tint multiplies the icon's magenta, so it can't get brighter
                iconColor = new Color(1f, 1f, 0f)
            };
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.master != null)
                {
                    CharacterMasterNotificationQueue.SendCustomNotification(pc.master, equipmentDef.equipmentIndex, overrideInfo);
                }
            }
        }
       
    }
}
