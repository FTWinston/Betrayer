using HarmonyLib;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Betrayer
{
    static class PlayerPositions
    {
        public static Func<long, bool> canPlayerSeeOthers = _ => true;

        public static Func<long, bool> canPlayerBeSeenByOthers = _ => true;

        [HarmonyPatch(typeof(ZNet), "UpdatePlayerList")]
        [HarmonyPrefix]
        private static bool UpdatePlayerList(ZNet __instance)
        {
            var m_players = __instance.GetPlayerList();
            var m_peers = __instance.GetPeers();

            m_players.Clear();
            
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                ZNet.PlayerInfo playerInfo = new ZNet.PlayerInfo();
                playerInfo.m_name = Game.instance.GetPlayerProfile().GetName();
                playerInfo.m_host = "";
                playerInfo.m_characterID = (ZDOID)typeof(ZNet).GetField("m_characterID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(__instance);
                playerInfo.m_publicPosition = __instance.IsReferencePositionPublic();
                //if (playerInfo.m_publicPosition)
                playerInfo.m_position = __instance.GetReferencePosition();
                m_players.Add(playerInfo);
            }
            foreach (ZNetPeer peer in m_peers)
            {
                if (peer.IsReady())
                {
                    ZNet.PlayerInfo playerInfo = new ZNet.PlayerInfo();
                    playerInfo.m_characterID = peer.m_characterID;
                    playerInfo.m_name = peer.m_playerName;
                    playerInfo.m_host = peer.m_socket.GetHostName();
                    playerInfo.m_publicPosition = peer.m_publicRefPos;
                    //if (playerInfo.m_publicPosition) // ALWAYS add this, not only if public!
                    playerInfo.m_position = peer.m_refPos;
                    m_players.Add(playerInfo);
                }
            }

            return false;
        }

        [HarmonyPatch(typeof(ZNet), "SendPlayerList")]
        [HarmonyPrefix]
        private static bool SendPlayerList(ZNet __instance)
        {
            UpdatePlayerList(__instance);

            var m_players = __instance.GetPlayerList();
            var m_peers = __instance.GetPeers();

            if (m_peers.Count <= 0)
                return false;

            ZPackage playersVisiblePackage = new ZPackage();
            playersVisiblePackage.Write(m_players.Count);

            ZPackage allHiddenPackage = new ZPackage();
            allHiddenPackage.Write(m_players.Count);

            foreach (ZNet.PlayerInfo player in m_players)
            {
                playersVisiblePackage.Write(player.m_name);
                playersVisiblePackage.Write(player.m_host);
                playersVisiblePackage.Write(player.m_characterID);

                if (canPlayerBeSeenByOthers(player.m_characterID.userID))
                {
                    playersVisiblePackage.Write(true);
                    playersVisiblePackage.Write(player.m_position);
                }
                else
                {
                    playersVisiblePackage.Write(false);
                }

                allHiddenPackage.Write(player.m_name);
                allHiddenPackage.Write(player.m_host);
                allHiddenPackage.Write(player.m_characterID);
                allHiddenPackage.Write(false);
            }

            foreach (ZNetPeer peer in m_peers)
            {
                if (peer.IsReady())
                {
                    var package = canPlayerSeeOthers(peer.m_uid) ? playersVisiblePackage : allHiddenPackage;
                    peer.m_rpc.Invoke("PlayerList", (object)package);
                }
            }

            return false;
        }
    }
}
