using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnboundLib.Utils.UI;
using UnityEngine;

namespace StartNCards
{
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("pykess.rounds.plugins.pickncards", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(ModId, ModName, "0.0.2")]
    [BepInProcess("Rounds.exe")]
    public class StartNCards : BaseUnityPlugin
    {
        private const string ModId = "darthbinks.rounds.plugins.startncards";
        private const string ModName = "Start N Cards";
        private const string CompatibilityModName = "StartNCards";

        private const int maxPicks = 10;

        private const string loggingPrefix = "[StartNCards]: ";

        internal static StartNCards instance;

        public static ConfigEntry<int> PicksConfig;
        public static ConfigEntry<float> DelayConfig;
        internal static int picks;
        internal static float delay = 0.1f;

        internal static bool lockPickQueue = false;
        internal static List<int> playerIDsToPick = new List<int>() { };
        internal static bool extraPicksInProgress = false;

        internal static bool transmittedStartingPicks = false;
        internal static bool transmittedPickNCardsPicks = false;
        internal static int pickNCardsStoredPicks = 0;
        

        private void Awake()
        {
            StartNCards.instance = this;

            // bind configs with BepInEx
            PicksConfig = Config.Bind(CompatibilityModName, "Picks", 1, "Total number of card picks per player at the beginning of the match");

            // apply patches
            new Harmony(ModId).PatchAll();
        }

        private void Start()
        {
            // call settings as to not orphan them
            picks = PicksConfig.Value;

            // add credits
            Unbound.RegisterCredits("Start N Cards", new string[] { "Darth Binks" }, new string[] { "GitHub" }, new string[] { "https://github.com/mclaager/StartNCards" });

            // add GUI to modoptions menu
            Unbound.RegisterMenu(ModName, () => { }, this.NewGUI, null, false);
            
            // handshake to sync settings
            Unbound.RegisterHandshake(StartNCards.ModId, this.OnHandShakeCompleted);

            // hook for picking N cards
            GameModeManager.AddHook(GameModeHooks.HookPickStart, StartNCards.PickStart, GameModeHooks.Priority.First);

            // hook for resetting
            GameModeManager.AddHook(GameModeHooks.HookGameStart, StartNCards.GameStart, GameModeHooks.Priority.First);
        }

        private void OnHandShakeCompleted()
        {
            if (PhotonNetwork.IsMasterClient)
            {
                NetworkingManager.RPC_Others(typeof(StartNCards), nameof(SyncSettings), new object[] { StartNCards.picks });
            }
        }

        [UnboundRPC]
        private static void SyncSettings(int host_picks)
        {
            StartNCards.picks = host_picks;
        }

        private void NewGUI(GameObject menu)
        {

            MenuHandler.CreateText(ModName+" Options", menu, out TextMeshProUGUI _, 60);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);
            void PicksChanged(float val)
            {
                StartNCards.PicksConfig.Value = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp(val,0f,(float)StartNCards.maxPicks));
                StartNCards.picks = StartNCards.PicksConfig.Value;
                OnHandShakeCompleted();
            }
            MenuHandler.CreateSlider("Number of cards to start the game with", menu, 30, 0f, (float)StartNCards.maxPicks, StartNCards.PicksConfig.Value, PicksChanged, out UnityEngine.UI.Slider _, true);
            MenuHandler.CreateText(" ", menu, out TextMeshProUGUI _, 30);

        }

        [UnboundRPC]
        public static void RPC_RequestSync(int requestingPlayer)
        {
            NetworkingManager.RPC(typeof(StartNCards), nameof(StartNCards.RPC_SyncResponse), requestingPlayer, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        [UnboundRPC]
        public static void RPC_SyncResponse(int requestingPlayer, int readyPlayer)
        {
            if (PhotonNetwork.LocalPlayer.ActorNumber == requestingPlayer)
            {
                StartNCards.instance.RemovePendingRequest(readyPlayer, nameof(StartNCards.RPC_RequestSync));
            }
        }

        private IEnumerator WaitForSyncUp()
        {
            if (PhotonNetwork.OfflineMode)
            {
                yield break;
            }
            yield return this.SyncMethod(nameof(StartNCards.RPC_RequestSync), null, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        internal static IEnumerator ResetPickQueue()
        {
            if (!StartNCards.extraPicksInProgress)
            {
                StartNCards.playerIDsToPick = new List<int>() { };
                StartNCards.lockPickQueue = false;
            }
            yield break;
        }

        internal static IEnumerator PickStart(IGameModeHandler handler)
        {
            yield return StartNCards.AlterPickNCardsForCompatibilty();
            yield return StartNCards.ResetPickQueue();
            yield break;
        }

        internal static IEnumerator GameStart(IGameModeHandler gm)
        {
            StartNCards.transmittedStartingPicks = false;
            StartNCards.transmittedPickNCardsPicks = false;

            yield break;
        }

        internal static IEnumerator AlterPickNCardsForCompatibilty()
        {
            if (!StartNCards.extraPicksInProgress && (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode))
            {
                // Temporarily disable PickNCards to prevent it from affecting first pick by settings pick count to 0
                if (!StartNCards.transmittedStartingPicks)
                {
#if DEBUG
                    UnityEngine.Debug.Log(loggingPrefix + $"Transmitting to everyone's PickNCards starting picks of {StartNCards.picks}.");
#endif
                    // Store PickNCards current value for later use
                    var picks = PickNCards.PickNCards.PicksConfig.Value;
                    pickNCardsStoredPicks = picks;

                    var draws = DrawNCards.DrawNCards.NumDrawsConfig.Value;

                    // Manually network Sync settings to everyone as PickNCards picks is internal and sync is private
                    NetworkingManager.RPC(typeof(PickNCards.PickNCards), "SyncSettings", new object[] { StartNCards.picks, draws });

                    yield return new WaitForSecondsRealtime(0.1f);

                    StartNCards.transmittedStartingPicks = true;
                }
                // Re-enable PickNCards
                else if (!StartNCards.transmittedPickNCardsPicks)
                {
#if DEBUG
                    UnityEngine.Debug.Log(loggingPrefix + $"Resetting everyone's PickNCards picks to {pickNCardsStoredPicks}.");
#endif
                    var draws = DrawNCards.DrawNCards.NumDrawsConfig.Value;

                    // Manually network Sync settings to everyone as PickNCards picks is internal and sync is private
                    NetworkingManager.RPC(typeof(PickNCards.PickNCards), "SyncSettings", new object[] { pickNCardsStoredPicks, draws });

                    yield return new WaitForSecondsRealtime(0.1f);

                    StartNCards.transmittedPickNCardsPicks = true;
                }
            }

            yield break;
        }
    }
}

