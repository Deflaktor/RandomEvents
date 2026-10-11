using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

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

        public static void AnnounceEvent()
        {
            int length = 10;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_EVENT_{r}");
            Send($"<size=18px><style=cEvent>{eventText}</color></size>");
        }

        public static void AnnounceDoubleEvent()
        {
            int length = 5;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_DOUBLE_EVENT_{r}");
            Send($"<size=22px><style=cEvent>{eventText}</color></size>");
        }
        public static void AnnounceTripleEvent()
        {
            int length = 3;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_TRIPLE_EVENT_{r}");
            Send($"<size=26px><style=cEvent>{eventText}</color></size>");
        }
        public static void AnnounceEventConclusion()
        {
            int length = 12;
            int r = rand.Next(length);
            var eventText = Language.GetStringFormatted($"ANNOUNCE_EVENT_CONCLUDE_{r}");
            Send($"<size=18px><style=cEvent>{eventText}</color></size>");
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
       
    }
}
