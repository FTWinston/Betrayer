using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Betrayer.Powers
{
    [BepInPlugin("com.ftwinston.valheim.betrayer.eternalnight", "Eternal Night", "0.1.0.0")]
    public class EternalNightPlugin : BaseUnityPlugin
    {
        public bool IsActive { get; private set; } = true;
        public void Activate()
        {
            if (CanActivate)
            {
                mCurrentState = EternalNightState.Initial;
                IsActive = true;
            }
        }
        public void Deactivate()
        {
            if (CanActivate) IsActive = false;
        }
        public bool CanActivate => EnvMan.instance.IsDay();
        public void Awake()
        {
            Harmony.CreateAndPatchAll(GetType());

        }

        private bool IsReady()
        {
            if (ZNet.instance == null) return false;
            if (EnvMan.instance == null) return false;
            if (Player.m_localPlayer == null) return false;
            return true;
        }

        private enum EternalNightState
        {
            Initial = 0,
            Inactive = 1,
            Active = 2,
            SkipToMorning = 3,
            SkipNightEntirely = 4
        }
        private static EternalNightState mCurrentState;

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SkipToMorning))]
        [HarmonyPrefix] 
        private static void SkipToMorning(EnvMan __instance)
        {
            if (mCurrentState == EternalNightState.Active)
            {
                mCurrentState = EternalNightState.SkipToMorning;
            } else
            {
                mCurrentState = EternalNightState.SkipNightEntirely;
            }
        }

        [HarmonyPatch(typeof(Game), "Start")]
        [HarmonyPostfix]
        static void GameStart()
        {
            Debug.Log($"Game Start");
            mCurrentState = EternalNightState.Initial;

            // This is run every time the server starts, so we don't really want it.
            // We only want when a game actually starts from the beginning.
        }

        //[HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
        //[HarmonyPrefix] 
        //private static void ShowTimeOnInteractWithBed(Bed __instance, Humanoid human, bool repeat)
        //{
        //    var owner = __instance.GetComponent<ZNetView>().GetZDO().GetLong("owner");
        //    GetDateInfo(out var time, out var day, out var dayLengthSec, out var dayFraction);
        //    Debug.Log($"PowerHandlerPlugin: day: {day}, frac:{dayFraction:0.00}, time: {time:0}");
        //    if (Game.instance.GetPlayerProfile().GetPlayerID() == owner)
        //    {
        //        int hour = Mathf.FloorToInt(dayFraction * 24);
        //        float minute = ((dayFraction * 24) - hour) * 60;
        //        human.Message(MessageHud.MessageType.TopLeft, $"The Time is {hour % 12:00}:{minute:00} {((hour < 12) ? "am" : "pm")}");
        //    } 
        //}

        private double lastDay = 0;
        public static void GetDateInfo(out double time, out double day, out long dayLengthSec, out float dayFraction)
        {
            time = ZNet.instance.GetTimeSeconds();
            day = EnvMan.instance.GetDay(time);
            dayLengthSec = EnvMan.instance.m_dayLengthSec;
            dayFraction = GetDayFraction(time, dayLengthSec);
        }

        private string lastCollapsedLog;
        private int sameCallCount = 0;
        private void SendCollapsedLog(string log)
        {
            if (log == lastCollapsedLog)
            {
                sameCallCount++;
                if (sameCallCount < 30)
                {
                    return;
                }
            }
            sameCallCount = 0;
            lastCollapsedLog = log;
            Debug.Log($"PowerHandlerPlugin: {log}");
        } 

        void FixedUpdate()
        {
            if (!IsReady()) return;
            if (!ZNet.instance.IsServer())
            {
                return;
            } 
            if (!IsActive) return;
            GetDateInfo(out var time, out var day, out var dayLengthSec, out var dayFraction);
            switch (mCurrentState)
            {
                case EternalNightState.Initial:
                    mCurrentState = EternalNightState.Inactive;
                    lastDay = day;
                    //Uncomment below to set time to just before midnight on Day 2
                    //Debug.LogWarning("Set Initial Time");
                    //ZNet.instance.SetNetTime(3550); //Reset to just before midnight  
                    //lastDay = 1;
                    break; 
                case EternalNightState.Inactive: 
                    if (lastDay < day && EnvMan.instance.IsNight())
                    {
                        mCurrentState = EternalNightState.Active;
                        Debug.LogWarning($"The Eternal Night Begins");
                        Utils.MessageAll(MessageHud.MessageType.Center, "The Eternal Night has fallen");
                        lastDay = day;
                    }
                    break;
                case EternalNightState.Active:
                    ZNet.instance.SetNetTime(day * dayLengthSec); //Reset to midnight
                    break;
                case EternalNightState.SkipToMorning:
                    if (!EnvMan.instance.IsNight())
                    {
                        Utils.MessageAll(MessageHud.MessageType.Center, "You have rested for what feels like an eternity...");
                        Debug.LogWarning($"The Eternal Night Has Ended");
                        mCurrentState = EternalNightState.Inactive;
                    }
                    break;
                case EternalNightState.SkipNightEntirely:
                    if (!EnvMan.instance.IsNight() && day > lastDay)
                    { 
                        Debug.LogWarning($"Skipped the Eternal Night");
                        lastDay = day;
                        mCurrentState = EternalNightState.Inactive;
                    }
                    break;
            }
            SendCollapsedLog($"State: {mCurrentState}, is night? {EnvMan.instance.IsNight()}, day: {lastDay} > {day}, frac:{dayFraction:0.00}, time: {time:0}");

        }

        private static float RescaleDayFraction(float fraction)
        {
            fraction = (double)fraction < 0.150000005960464 || (double)fraction > 0.850000023841858 ? ((double)fraction >= 0.5 ? (float)(0.75 + ((double)fraction - 0.850000023841858) / 0.150000005960464 * 0.25) : (float)((double)fraction / 0.150000005960464 * 0.25)) : (float)(0.25 + ((double)fraction - 0.150000005960464) / 0.699999988079071 * 0.5);
            return fraction;
        }

        private static float GetDayFraction(double m_totalSeconds, long m_dayLengthSec) {
            return RescaleDayFraction(Mathf.Clamp01((float)(m_totalSeconds * 1000.0 % (double)(m_dayLengthSec * 1000L) / 1000.0) / (float)m_dayLengthSec));

        }
    }
}

