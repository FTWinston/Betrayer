using HarmonyLib;
using System.Collections.Generic;

namespace Betrayer
{
    static class PlayerPositions
    {
        public static readonly HashSet<long> peersWhoCanSeeAll = new HashSet<long>();

        [HarmonyPatch(typeof(ZNet), "UpdatePlayerList")]
        [HarmonyPrefix]
        private static bool UpdatePlayerList(ZNet __instance)
        {
            var m_players = __instance.GetPlayerList();
            var m_peers = __instance.GetConnectedPeers();

            m_players.Clear();
            /*
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                ZNet.PlayerInfo playerInfo = new ZNet.PlayerInfo();
                playerInfo.m_name = Game.instance.GetPlayerProfile().GetName();
                playerInfo.m_host = "";
                playerInfo.m_characterID = __instance.m_characterID;
                playerInfo.m_publicPosition = __instance.m_publicReferencePosition;
                //if (playerInfo.m_publicPosition)
                playerInfo.m_position = __instance.m_referencePosition;
                __instance.m_players.Add(playerInfo);
            }
            */
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
            var m_peers = __instance.GetConnectedPeers();

            if (m_peers.Count <= 0)
                return false;

            ZPackage seeAllPackage = new ZPackage();
            seeAllPackage.Write(m_players.Count);

            ZPackage seeNonePackage = new ZPackage();
            seeNonePackage.Write(m_players.Count);

            foreach (ZNet.PlayerInfo player in m_players)
            {
                seeAllPackage.Write(player.m_name);
                seeAllPackage.Write(player.m_host);
                seeAllPackage.Write(player.m_characterID);
                seeAllPackage.Write(true);
                seeAllPackage.Write(player.m_position);

                seeNonePackage.Write(player.m_name);
                seeNonePackage.Write(player.m_host);
                seeNonePackage.Write(player.m_characterID);
                seeNonePackage.Write(false);
            }

            foreach (ZNetPeer peer in m_peers)
            {
                if (peer.IsReady())
                {
                    var package = peersWhoCanSeeAll.Contains(peer.m_uid) ? seeAllPackage : seeNonePackage;
                    peer.m_rpc.Invoke("PlayerList", (object)package);
                }
            }

            return false;
        }
    }
}
