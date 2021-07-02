using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Betrayer
{
    [BepInPlugin("com.ftwinston.valheim.betrayer", "Betrayer", "0.1.0.0")]
    public class BetrayerPlugin : BaseUnityPlugin
    {
        public void Awake()
        {
            Harmony.CreateAndPatchAll(GetType());
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        static void PlayerSpawned(Player __instance)
        {
            Debug.Log("DUDE THIS PLAYER SPAWNED!");

            // Ah wait ... this is CLIENT SIDE?
            Game.instance.DiscoverClosestLocation("Dragonqueen", __instance.transform.position, "Collect Egg Here", (int)Minimap.PinType.Boss);
            // Might need to look into copying from RPC_DiscoverClosestLocation for server side stuff.

            // Probably not the right names
            Game.instance.DiscoverClosestLocation("Guardianstone", __instance.transform.position, "Return Egg Here", (int)Minimap.PinType.Ping);
            Game.instance.DiscoverClosestLocation("SacrificialStone", __instance.transform.position, "Return Egg Here", (int)Minimap.PinType.Ping);

            __instance.Message(MessageHud.MessageType.Center, "Work together to bring a wyvern egg\nfrom Moder's altar to the sacrificial stones.\nAt the first nightfall, one of you will be chosen\nto betray the others...");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnRespawn))]
        [HarmonyPostfix]
        static void PlayerRespawned(Player __instance)
        {
            Debug.Log("DUDE THIS PLAYER RESPAWNED! So, putting them in ghost mode...");
            __instance.SetGhostMode(true);
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

        const int minimumPlayersToStart = 3;
        const string goalItem = "DragonEgg";

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

        [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
        [HarmonyPrefix]
        static bool BossSpawnerInteract(OfferingBowl __instance, Humanoid user, ItemDrop.ItemData item)
        {
            Debug.Log("Boss spawner interact");

            //if (boss type isnt the selected one)
                //return true;

            user.GetInventory().AddItem(goalItem, 1, 1, 0, user.GetOwner(), user.name);

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
