using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Betrayer
{
    [BepInPlugin("com.ftwinston.valheim.betrayer", "Betrayer", "0.1.0.0")]
    public class BetrayerPlugin : BaseUnityPlugin
    {
        // TODO: put these in config settings
        const int minimumPlayersToStart = 3;
        const string targetBossSpawnerID = "Dragonqueen";
        const string targetBossName = "Moder"; // TODO: is there a resource string for this?
        const string goalItemID = "DragonEgg";
        const string goalItemName = "wyvern egg"; // TODO: is there a resource string for this?

        public void Awake()
        {
            Harmony.CreateAndPatchAll(GetType());
        }

        /*
        // These methods run only on the client.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        static void PlayerSpawned(Player __instance)
        {
            Debug.Log("DUDE THIS PLAYER SPAWNED!");

            // Ah wait ... this is CLIENT SIDE?
            Game.instance.DiscoverClosestLocation(targetBossSpawnerID, __instance.transform.position, $"Collect {goalItemName} here", (int)Minimap.PinType.Boss);
            // Might need to look into copying from RPC_DiscoverClosestLocation for server side stuff.

            // Probably not the right names
            Game.instance.DiscoverClosestLocation("Guardianstone", __instance.transform.position, $"Return {goalItemName} here", (int)Minimap.PinType.Ping);
            Game.instance.DiscoverClosestLocation("SacrificialStone", __instance.transform.position, $"Return {goalItemName} here", (int)Minimap.PinType.Ping);

            __instance.Message(MessageHud.MessageType.Center, $"Work together to bring a {goalItemName}\nfrom {targetBossName}'s altar to the sacrificial stones.\nAt nightfall, one of you will be chosen\nto betray the others...");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnRespawn))]
        [HarmonyPostfix]
        static void PlayerRespawned(Player __instance)
        {
            Debug.Log("DUDE THIS PLAYER RESPAWNED! So, putting them in ghost mode...");
            __instance.SetGhostMode(true);
        }
        */

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPostfix]
        static void PlayerSpawned(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
            if (characterID == ZDOID.None)
            {
                // Player died, just been unlinked from their character.
                return;
            }

            // This shows 0 and 0... is it just too early in the life cycle?
            Debug.Log($"Player {characterID} spawned, got {__instance.GetNrOfPlayers()} player(s)");

            var lookupPlayer = Player.GetPlayer(characterID.userID);
            Debug.Log($"Lookup player {(lookupPlayer == null ? "is" : "is not ")} null");

            foreach (var playerInfo in __instance.GetPlayerList())
            {
                var peer = __instance.GetPeerByPlayerName(playerInfo.m_name);

                // playerInfo.m_characterID is still ZDOID.None at this point
                if (peer.m_characterID == characterID)
                {
                    Debug.Log($"The player that just spawned was {playerInfo.m_name}, and their peer is {peer.m_uid} / {peer.m_characterID}");
                }
                else
                {
                    Debug.Log($"An unrelated player is {playerInfo.m_name}, with ID {playerInfo.m_characterID}, and their peer is {peer.m_uid} / {peer.m_characterID}");
                }
            }

            Debug.Log("Done looping over players");

            // This doesn't do it either ... guess this method is just too early in the "spawn" process.
            //Player.MessageAllInRange(Vector3.zero, 1000000, MessageHud.MessageType.Center, "YO HO HO");

            SendTargetLocation(characterID.userID);

            SendMessage(characterID, MessageHud.MessageType.TopLeft, $"Work together to bring a {goalItemName}\nfrom {targetBossName}'s altar to the sacrificial stones.\nAt nightfall, one of you will be chosen\nto betray the others...");

            //__instance.GetPlayerList();

            //var __instance.GetPeerByPlayerName("name").m_characterID
        }

        private static void SendMessage(ZDOID targetID, MessageHud.MessageType type, string message)
        {
            Debug.Log($"Sending message to {targetID}: {message}");
            
            // TODO: this doesn't seem to be showing actually... is the call just too soon after spawning?
            ZRoutedRpc.instance.InvokeRoutedRPC(targetID.userID, targetID, nameof(Player.Message), (object)(int)type, (object)message);
        }

        private static void SendMessageToAll(MessageHud.MessageType type, string message)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(nameof(Player.Message), (object)(int)type, (object)message);
        }

        private static void SendTargetLocation(long userID)
        {
            ZoneSystem.LocationInstance closest;
            if (ZoneSystem.instance.FindClosestLocation(targetBossSpawnerID, Vector3.zero, out closest))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(userID, "DiscoverLocationRespons", (object)$"Collect {goalItemName} here", (object)(int)Minimap.PinType.Boss, (object)closest.m_position);
            }
            else
            {
                Debug.LogWarning($"Failed to find location of {targetBossSpawnerID} for spawning player");
            }
        }

        [HarmonyPatch(typeof(Game), "Start")]
        [HarmonyPostfix]
        static void GameStart()
        {
            Debug.Log($"Game Start");
            betrayerPlayerName = null;
            betrayerPlayerID = ZDOID.None;

            // This is run every time the server starts, so we don't really want it.
            // We only want when a game actually starts from the beginning.
        }

        static string betrayerPlayerName = null;
        static ZDOID betrayerPlayerID = ZDOID.None;

        /*
        [HarmonyPatch(typeof(Game), "Update")]
        [HarmonyPostfix]
        static void GameTick()
        {
            var elapsedTime = ZNet.instance.GetTimeSeconds();

            if (betrayerPlayerName == null && elapsedTime >= allocationDelay)
                AllocateBetrayer();
        }
        */

        [HarmonyPatch(typeof(EnvMan), "OnEvening")]
        [HarmonyPostfix]
        static void EveningFalls()
        {
            if (betrayerPlayerName == null && Player.m_localPlayer == null)
                AllocateBetrayer();
        }

        public static bool IsBetrayer(Player player)
        {
            return player.GetZDOID() == betrayerPlayerID;
        }

        private static bool AllocateBetrayer()
        {
            var allPlayers = Player.GetAllPlayers();

            if (allPlayers.Count < minimumPlayersToStart)
            {
                return false;
            }

            var betrayerPlayer = allPlayers[Random.Range(0, allPlayers.Count - 1)];

            betrayerPlayerName = betrayerPlayer.m_name;

            foreach (var player in allPlayers)
            {
                string message = IsBetrayer(player)
                    ? "You must <color=red>betray</color> your companions.\nStop them from retrieving the wyvern egg!"
                    : "One of your companions has been chosen\nto <color=red>betray</color> you.\nYou are not the betrayer!";

                player.Message(MessageHud.MessageType.Center, message);
            }

            return true;
        }

        //public bool Interact(Humanoid user, bool hold);
        //public bool UseItem(Humanoid user, ItemDrop.ItemData item);


        [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
        [HarmonyPrefix]
        static bool BossSpawnerInteract(OfferingBowl __instance, Humanoid user, bool hold)
        {
            // TODO: ensure this is checking the correct thing...
            if (__instance.m_name != targetBossSpawnerID)
            {
                Debug.Log($"Wrong boss spawner interact: {__instance.m_name} found, {targetBossSpawnerID} needed");
                return true;
            }

            Debug.Log("Correct boss spawner interact");
            return false;
        }

        [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem))]
        [HarmonyPrefix]
        static bool BossSpawnerUse(OfferingBowl __instance, Humanoid user, ItemDrop.ItemData item)
        {
            // TODO: ensure this is checking the correct thing...
            if (__instance.m_name != targetBossSpawnerID)
            {
                Debug.Log($"Wrong boss spawner use: {__instance.m_name} found, {targetBossSpawnerID} needed");
                return true;
            }

            Debug.Log("Correct boss spawner use");

            user.GetInventory().AddItem(goalItemID, 1, 1, 0, user.GetOwner(), user.name);

            // Play effect
            if ((bool)(Object)__instance.m_itemSpawnPoint)
                __instance.m_fuelAddedEffects.Create(__instance.m_itemSpawnPoint.position, __instance.transform.rotation);

            // If no betrayer already allocated, definitely do so now...
            if (betrayerPlayerName == null && !AllocateBetrayer())
                user.Message(MessageHud.MessageType.Center, "$msg_offerdone");

            return false; // Returning false disables the original method.
        }

        // TODO: detect item dropping back at the start ... somehow

        /*
        To save world data, patch ZPackage.GetArray, only when called from within World.SaveWorldMetaData, to also save "the betrayer" if that's been set.
        To load world data, patch ZPackage.ReadInt, only when called for the 2nd time from within World.Loadworld, to also load "the betrayer" if specified there.
        */

        /*
        Have the raven introduce everything you need. Raven.Spawn and/or Raven.Talk are the methods to override there...

        Oh dear ... is he only handled locally? He does this check:
        
          private void CheckSpawn()
          {
            if ((UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) null)
              return;

            ...
          }
        */
    }
}
