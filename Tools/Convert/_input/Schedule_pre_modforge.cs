using BepInEx;
using GameCreator;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Common.Audio;
using GameCreator.Runtime.Common.UnityUI;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Dialogue.UnityUI;
using GameCreator.Runtime.Variables;
using GameCreator.Runtime.VisualScripting;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.XPath;
using TMPro;
using TransitionsPlusDemos;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;

namespace SMSAndroidsCore
{
    [BepInPlugin(pluginGuid, Core.pluginName + " - Schedule", Core.pluginVersion)]
    internal class Schedule : BaseUnityPlugin
    {
        #region Plugin Info
        public const string pluginGuid = "treboy.starmakerstory.smsandroidscore.schedule";
        #endregion

        // Character location variables - default to their rooms
        public static string amberDefaultLocation;
        public static string amberLocation;
        public static string amberHHLocation;
        public static GameObject amberHHOutfit;
        public static string claireDefaultLocation;
        public static string claireLocation;
        public static string claireHHLocation;
        public static GameObject claireHHOutfit;
        public static string sarahDefaultLocation;
        public static string sarahLocation;
        public static string sarahHHLocation;
        public static GameObject sarahHHOutfit;

        public static string anisDefaultLocation;
        public static string anisLocation;
        public static string anisLocationPrevious;
        public static string anisHHLocation;
        public static GameObject anisHHOutfit;
        public static string centiDefaultLocation;
        public static string centiLocation;
        public static string centiHHLocation;
        public static GameObject centiHHOutfit;
        public static string dorothyDefaultLocation;
        public static string dorothyLocation;
        public static string dorothyHHLocation;
        public static GameObject dorothyHHOutfit;
        public static string eleggDefaultLocation;
        public static string eleggLocation;
        public static string eleggHHLocation;
        public static GameObject eleggHHOutfit;
        public static string frimaDefaultLocation;
        public static string frimaLocation;
        public static string frimaHHLocation;
        public static GameObject frimaHHOutfit;
        public static string guiltyDefaultLocation;
        public static string guiltyLocation;
        public static string guiltyHHLocation;
        public static GameObject guiltyHHOutfit;
        public static string helmDefaultLocation;
        public static string helmLocation;
        public static string helmHHLocation;
        public static GameObject helmHHOutfit;
        public static string maidenDefaultLocation;
        public static string maidenLocation;
        public static string maidenHHLocation;
        public static GameObject maidenHHOutfit;
        public static string maryDefaultLocation;
        public static string maryLocation;
        public static string maryHHLocation;
        public static GameObject maryHHOutfit;
        public static string mastDefaultLocation;
        public static string mastLocation;
        public static string mastHHLocation;
        public static GameObject mastHHOutfit;
        public static string neonDefaultLocation;
        public static string neonLocation;
        public static string neonHHLocation;
        public static GameObject neonHHOutfit;
        public static string pepperDefaultLocation;
        public static string pepperLocation;
        public static string pepperHHLocation;
        public static GameObject pepperHHOutfit;
        public static string rapiDefaultLocation;
        public static string rapiLocation;
        public static string rapiHHLocation;
        public static GameObject rapiHHOutfit;
        public static string rosannaDefaultLocation;
        public static string rosannaLocation;
        public static string rosannaHHLocation;
        public static GameObject rosannaHHOutfit;
        public static string sakuraDefaultLocation;
        public static string sakuraLocation;
        public static string sakuraHHLocation;
        public static GameObject sakuraHHOutfit;
        public static string toveDefaultLocation;
        public static string toveLocation;
        public static string toveHHLocation;
        public static GameObject toveHHOutfit;
        public static string viperDefaultLocation;
        public static string viperLocation;
        public static string viperHHLocation;
        public static GameObject viperHHOutfit;
        public static string yanDefaultLocation;
        public static string yanLocation;
        public static string yanHHLocation;
        public static GameObject yanHHOutfit;

        public static string snekDefaultLocation;
        public static string snekLocation;

        // Harbor Home roaming system
        private static readonly string[] hhCharacterNames = new string[]
        {
            "Amber", "Claire", "Sarah",
            "Anis", "Centi", "Dorothy", "Elegg", "Frima", "Guilty", "Helm",
            "Maiden", "Mary", "Mast", "Neon", "Pepper", "Rapi", "Rosanna",
            "Sakura", "Tove", "Viper", "Yan"
        };

        // Room names (excluding Entrance)
        private static readonly string[] hhRoomNames = new string[]
        {
            "HarborHomeBathroom", "HarborHomeBedroom", "HarborHomeCloset",
            "HarborHomeKitchen", "HarborHomeLivingRoom", "HarborHomePool"
        };

        // Per-character timers and state for HH roaming
        private static Dictionary<string, float> hhNextRoamTime = new Dictionary<string, float>();
        private static Dictionary<string, bool> hhFirstPassDone = new Dictionary<string, bool>();
        
        // First HH roaming pass delay tracking
        private static bool hhFirstPassTriggered = false;
        private static float hhFirstPassTriggerTime = 0f;
        private const float hhFirstPassDelay = 3f;  // 3 seconds delay

        public static double day;
        public static bool loadedSchedule = false;
        private static bool hhOutfitsInitialized = false;

        public void Update()
        {
            if (Core.currentScene.name == "CoreGameScene")
            {
                if (!loadedSchedule && Core.loadedCore)
                {
                    amberDefaultLocation = "MountainLab";
                    claireDefaultLocation = "GiftShopInterior";
                    sarahDefaultLocation = "Unknown";

                    anisDefaultLocation = "MountainLabRoomNikkeAnis";
                    centiDefaultLocation = "MountainLabRoomNikkeCenti";
                    dorothyDefaultLocation = "MountainLabRoomNikkeDorothy";
                    eleggDefaultLocation = "MountainLabRoomNikkeElegg";
                    frimaDefaultLocation = "MountainLabRoomNikkeFrima";
                    guiltyDefaultLocation = "MountainLabRoomNikkeGuilty";
                    helmDefaultLocation = "MountainLabRoomNikkeHelm";
                    maidenDefaultLocation = "MountainLabRoomNikkeMaiden";
                    maryDefaultLocation = "MountainLabRoomNikkeMary";
                    mastDefaultLocation = "MountainLabRoomNikkeMast";
                    neonDefaultLocation = "MountainLabRoomNikkeNeon";
                    pepperDefaultLocation = "MountainLabRoomNikkePepper";
                    rapiDefaultLocation = "MountainLabRoomNikkeRapi";
                    rosannaDefaultLocation = "MountainLabRoomNikkeRosanna";
                    sakuraDefaultLocation = "MountainLabRoomNikkeSakura";
                    toveDefaultLocation = "MountainLabRoomNikkeTove";
                    viperDefaultLocation = "MountainLabRoomNikkeViper";
                    yanDefaultLocation = "MountainLabRoomNikkeYan";

                    snekDefaultLocation = "Unknown";

                    InitializeCharacterLocations();
                    InitializeHHRoaming();
                    Logger.LogInfo("----- SCHEDULE LOADED -----");
                    loadedSchedule = true;
                }
                if (loadedSchedule)
                {
                    if (Core.loadedBases)
                    {
                        // Initialize HH outfits to default character busts once Characters are loaded
                        if (!hhOutfitsInitialized)
                        {
                            amberHHOutfit = Characters.amber;
                            claireHHOutfit = Characters.claire;
                            sarahHHOutfit = Characters.sarah;
                            anisHHOutfit = Characters.anis;
                            centiHHOutfit = Characters.centi;
                            dorothyHHOutfit = Characters.dorothy;
                            eleggHHOutfit = Characters.elegg;
                            frimaHHOutfit = Characters.frima;
                            guiltyHHOutfit = Characters.guilty;
                            helmHHOutfit = Characters.helm;
                            maidenHHOutfit = Characters.maiden;
                            maryHHOutfit = Characters.mary;
                            mastHHOutfit = Characters.mast;
                            neonHHOutfit = Characters.neon;
                            pepperHHOutfit = Characters.pepper;
                            rapiHHOutfit = Characters.rapi;
                            rosannaHHOutfit = Characters.rosanna;
                            sakuraHHOutfit = Characters.sakura;
                            toveHHOutfit = Characters.tove;
                            viperHHOutfit = Characters.viper;
                            yanHHOutfit = Characters.yan;
                            hhOutfitsInitialized = true;
                            Debug.Log("[Schedule] HH outfits initialized to default busts");
                        }

                        // Start timer for first HH roaming pass once Core.loadedBases is true
                        if (!hhFirstPassTriggered)
                        {
                            if (hhFirstPassTriggerTime == 0f)
                            {
                                hhFirstPassTriggerTime = Time.time;
                                Debug.Log("[Schedule] Starting HH roaming first pass timer (3s delay)");
                            }
                            else if (Time.time >= hhFirstPassTriggerTime + hhFirstPassDelay)
                            {
                                UpdateHHRoaming();
                                hhFirstPassTriggered = true;
                                Debug.Log("[Schedule] HH roaming first pass triggered after delay");
                            }
                        }
                        // Continue normal roaming updates after first pass
                        else if (hhFirstPassTriggered)
                        {
                            UpdateHHRoaming();
                        }

                        if (anisLocation != anisLocationPrevious)
                        {
                            anisLocationPrevious = anisLocation;
                            Characters.anisNPCHHBedleftDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHBedrightDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHChangingleftDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHChangingrightDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHCouchleftDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHCouchrightDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHFridgeDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHSinkDefault.GetComponent<RandomChildActivator>().pickNewOnEnable = true;

                            Characters.anisNPCHHBedleftSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHBedrightSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHChangingleftSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHChangingrightSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHCouchleftSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHCouchrightSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHFridgeSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHSinkSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHTanningleftSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                            Characters.anisNPCHHTanningrightSwim.GetComponent<RandomChildActivator>().pickNewOnEnable = true;

                            Characters.anisNPCHHShowerNaked.GetComponent<RandomChildActivator>().pickNewOnEnable = true;
                        }
                        Characters.anisNPCHHBedleftDefault.gameObject.SetActive(anisLocation == "HarborHomeBedroomBedleft" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHBedrightDefault.gameObject.SetActive(anisLocation == "HarborHomeBedroomBedright" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHChangingleftDefault.gameObject.SetActive(anisLocation == "HarborHomeClosetChangingleft" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHChangingrightDefault.gameObject.SetActive(anisLocation == "HarborHomeClosetChangingright" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHCouchleftDefault.gameObject.SetActive(anisLocation == "HarborHomeLivingRoomCouchleft" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHCouchrightDefault.gameObject.SetActive(anisLocation == "HarborHomeLivingRoomCouchright" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHFridgeDefault.gameObject.SetActive(anisLocation == "HarborHomeKitchenFridge" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");
                        Characters.anisNPCHHSinkDefault.gameObject.SetActive(anisLocation == "HarborHomeKitchenSink" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Default");

                        Characters.anisNPCHHBedleftSwim.gameObject.SetActive(anisLocation == "HarborHomeBedroomBedleft" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHBedrightSwim.gameObject.SetActive(anisLocation == "HarborHomeBedroomBedright" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHChangingleftSwim.gameObject.SetActive(anisLocation == "HarborHomeClosetChangingleft" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHChangingrightSwim.gameObject.SetActive(anisLocation == "HarborHomeClosetChangingright" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHCouchleftSwim.gameObject.SetActive(anisLocation == "HarborHomeLivingRoomCouchleft" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHCouchrightSwim.gameObject.SetActive(anisLocation == "HarborHomeLivingRoomCouchright" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHFridgeSwim.gameObject.SetActive(anisLocation == "HarborHomeKitchenFridge" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHSinkSwim.gameObject.SetActive(anisLocation == "HarborHomeKitchenSink" && SaveManager.GetString("HarborHome_Outfit_Anis") == "Swim");
                        Characters.anisNPCHHTanningleftSwim.gameObject.SetActive(anisLocation == "HarborHomePoolTanningleft");
                        Characters.anisNPCHHTanningrightSwim.gameObject.SetActive(anisLocation == "HarborHomePoolTanningright");

                        Characters.anisNPCHHShowerNaked.gameObject.SetActive(anisLocation == "HarborHomeBathroomShower");



                        if (Places.harborHomeKitchenLevel.activeSelf || Places.harborHomeLivingroomLevel.activeSelf || Places.harborHomePoolLevel.activeSelf || Places.harborHouseEntranceLevel.activeSelf)
                        {
                            Dialogues.audioShower.SetActive(false);
                            Dialogues.audioShowerQuiet.SetActive(false);
                        }
                        if (anisLocation == "HarborHomeBathroomShower")
                        {
                            if (Places.harborHomeBedroomLevel.activeSelf || Places.harborHomeClosetLevel.activeSelf)
                            {
                                Dialogues.audioShower.SetActive(false);
                                Dialogues.audioShowerQuiet.SetActive(true);
                            }   else if (Places.harborHomeBathroomLevel.activeSelf)
                            {
                                Dialogues.audioShower.SetActive(true);
                                Dialogues.audioShowerQuiet.SetActive(false);
                            }
                        } else if (Places.harborHomeBathroomLevel.activeSelf || Places.harborHomeBedroomLevel.activeSelf || Places.harborHomeClosetLevel.activeSelf || Places.harborHomeKitchenLevel.activeSelf ||
                            Places.harborHomeLivingroomLevel.activeSelf || Places.harborHomePoolLevel.activeSelf || Places.harborHouseEntranceLevel.activeSelf)
                        {
                            Dialogues.audioShower.SetActive(false);
                            Dialogues.audioShowerQuiet.SetActive(false);
                        }

                        if(amberLocation != Core.GetProxyVariableString("Location_Amber")) { Core.FindAndModifyProxyVariableString("Location_Amber", amberLocation); }
                        if(claireLocation != Core.GetProxyVariableString("Location_Claire")) { Core.FindAndModifyProxyVariableString("Location_Claire", claireLocation); }
                        if(sarahLocation != Core.GetProxyVariableString("Location_Sarah")) { Core.FindAndModifyProxyVariableString("Location_Sarah", sarahLocation); }

                        if(anisLocation != Core.GetProxyVariableString("Location_Anis")) { Core.FindAndModifyProxyVariableString("Location_Anis", anisLocation); }
                        if(centiLocation != Core.GetProxyVariableString("Location_Centi")) { Core.FindAndModifyProxyVariableString("Location_Centi", centiLocation); }
                        if(dorothyLocation != Core.GetProxyVariableString("Location_Dorothy")) { Core.FindAndModifyProxyVariableString("Location_Dorothy", dorothyLocation); }
                        if(eleggLocation != Core.GetProxyVariableString("Location_Elegg")) { Core.FindAndModifyProxyVariableString("Location_Elegg", eleggLocation); }
                        if(frimaLocation != Core.GetProxyVariableString("Location_Frima")) { Core.FindAndModifyProxyVariableString("Location_Frima", frimaLocation); }
                        if(guiltyLocation != Core.GetProxyVariableString("Location_Guilty")) { Core.FindAndModifyProxyVariableString("Location_Guilty", guiltyLocation); }
                        if(helmLocation != Core.GetProxyVariableString("Location_Helm")) { Core.FindAndModifyProxyVariableString("Location_Helm", helmLocation); }
                        if(maidenLocation != Core.GetProxyVariableString("Location_Maiden")) { Core.FindAndModifyProxyVariableString("Location_Maiden", maidenLocation); }
                        if(maryLocation != Core.GetProxyVariableString("Location_Mary")) { Core.FindAndModifyProxyVariableString("Location_Mary", maryLocation); }
                        if(mastLocation != Core.GetProxyVariableString("Location_Mast")) { Core.FindAndModifyProxyVariableString("Location_Mast", mastLocation); }
                        if(neonLocation != Core.GetProxyVariableString("Location_Neon")) { Core.FindAndModifyProxyVariableString("Location_Neon", neonLocation); }
                        if(pepperLocation != Core.GetProxyVariableString("Location_Pepper")) { Core.FindAndModifyProxyVariableString("Location_Pepper", pepperLocation); }
                        if(rapiLocation != Core.GetProxyVariableString("Location_Rapi")) { Core.FindAndModifyProxyVariableString("Location_Rapi", rapiLocation); }
                        if(rosannaLocation != Core.GetProxyVariableString("Location_Rosanna")) { Core.FindAndModifyProxyVariableString("Location_Rosanna", rosannaLocation); }
                        if(sakuraLocation != Core.GetProxyVariableString("Location_Sakura")) { Core.FindAndModifyProxyVariableString("Location_Sakura", sakuraLocation); }
                        if(toveLocation != Core.GetProxyVariableString("Location_Tove")) { Core.FindAndModifyProxyVariableString("Location_Tove", toveLocation); }
                        if(viperLocation != Core.GetProxyVariableString("Location_Viper")) { Core.FindAndModifyProxyVariableString("Location_Viper", viperLocation); }
                        if(yanLocation != Core.GetProxyVariableString("Location_Yan")) { Core.FindAndModifyProxyVariableString("Location_Yan", yanLocation); }
                    }
                }
            }
            if (Core.currentScene.name == "GameStart")
            {
                if (loadedSchedule)
                {
                    Logger.LogInfo("----- SCHEDULE UNLOADED -----");
                    loadedSchedule = false;
                    hhOutfitsInitialized = false;
                    hhFirstPassTriggered = false;
                    hhFirstPassTriggerTime = 0f;
                }
            }
        }

        private void InitializeCharacterLocations()
        {
            // Initialize current locations to defaults
            amberLocation = amberDefaultLocation;
            claireLocation = claireDefaultLocation;
            sarahLocation = sarahDefaultLocation;

            anisLocation = anisDefaultLocation;
            centiLocation = centiDefaultLocation;
            dorothyLocation = dorothyDefaultLocation;
            eleggLocation = eleggDefaultLocation;
            frimaLocation = frimaDefaultLocation;
            guiltyLocation = guiltyDefaultLocation;
            helmLocation = helmDefaultLocation;
            maidenLocation = maidenDefaultLocation;
            maryLocation = maryDefaultLocation;
            mastLocation = mastDefaultLocation;
            neonLocation = neonDefaultLocation;
            pepperLocation = pepperDefaultLocation;
            rapiLocation = rapiDefaultLocation;
            rosannaLocation = rosannaDefaultLocation;
            sakuraLocation = sakuraDefaultLocation;
            toveLocation = toveDefaultLocation;
            viperLocation = viperDefaultLocation;
            yanLocation = yanDefaultLocation;

            snekLocation = snekDefaultLocation;
        }

        public static void UpdateScheduleForDay()
        {
            switch (day)
            {
                case 1:
                    SetDay1Schedule();
                    break;
                case 2:
                    SetDay2Schedule();
                    break;
                case 3:
                    SetDay3Schedule();
                    break;
                case 4:
                    SetDay4Schedule();
                    break;
                case 5:
                    SetDay5Schedule();
                    break;
                default:
                    // Default to day 1 schedule for any other day value
                    SetDay1Schedule();
                    break;
            }
            
            Debug.Log($"Schedule updated for Day {day}");
            
            // Signal that the schedule was updated (visualizer will detect this)
            scheduleVersion++;
        }
        
        // Version counter that increments when schedule changes - visualizer can watch this
        public static int scheduleVersion = 0;

        #region Harbor Home Roaming System

        /// <summary>
        /// Initializes the HH roaming system, running the first pass for all eligible characters.
        /// </summary>
        private void InitializeHHRoaming()
        {
            hhNextRoamTime.Clear();
            hhFirstPassDone.Clear();

            foreach (string charName in hhCharacterNames)
            {
                hhFirstPassDone[charName] = false;
                hhNextRoamTime[charName] = 0f;
            }
        }

        /// <summary>
        /// Called every frame from Update. Handles the HH roaming logic for all characters.
        /// </summary>
        private static void UpdateHHRoaming()
        {
            // Collect all currently occupied HH positions across all characters
            HashSet<string> occupiedPositions = new HashSet<string>();
            foreach (string charName in hhCharacterNames)
            {
                string hhLoc = GetHHLocation(charName);
                if (!string.IsNullOrEmpty(hhLoc) && hhLoc != "HarborHomeLivingRoom")
                {
                    occupiedPositions.Add(hhLoc);
                }
            }

            foreach (string charName in hhCharacterNames)
            {
                if (!SaveManager.GetBool("HarborHome_Visit_" + charName))
                    continue;

                string currentLocation = GetCharacterLocation(charName);
                string defaultLocation = GetDefaultLocation(charName);

                // Only roam if the character is at their default location or already in an HH room
                if (currentLocation != defaultLocation && !currentLocation.StartsWith("HarborHome"))
                    continue;

                // First pass: run immediately
                if (!hhFirstPassDone[charName])
                {
                    string currentHHLoc = GetHHLocation(charName);
                    // Remove this character's old position from occupied set before reassigning
                    if (!string.IsNullOrEmpty(currentHHLoc) && currentHHLoc != "HarborHomeLivingRoom")
                    {
                        occupiedPositions.Remove(currentHHLoc);
                    }

                    string newLoc = PickRandomHHRoomPosition(charName, currentHHLoc, occupiedPositions);
                    SetHHLocation(charName, newLoc);
                    if (newLoc != "HarborHomeLivingRoom")
                    {
                        occupiedPositions.Add(newLoc);
                    }

                    hhNextRoamTime[charName] = Time.time + UnityEngine.Random.Range(15f, 45f);
                    hhFirstPassDone[charName] = true;

                    // Apply the HH location override
                    SetCharacterLocation(charName, newLoc);
                    Debug.Log($"[HHRoam] {charName} first pass -> {newLoc}");
                    continue;
                }

                // Subsequent passes: check timer AND that the current room level is not active
                if (Time.time < hhNextRoamTime[charName])
                    continue;

                string curHHLoc = GetHHLocation(charName);
                GameObject currentRoomLevel = GetHHRoomLevelGO(curHHLoc);
                if (currentRoomLevel != null && currentRoomLevel.activeSelf)
                    continue; // Player is looking at the room, don't swap

                // Remove this character's old position from occupied set before reassigning
                if (!string.IsNullOrEmpty(curHHLoc) && curHHLoc != "HarborHomeLivingRoom")
                {
                    occupiedPositions.Remove(curHHLoc);
                }

                string nextLoc = PickRandomHHRoomPosition(charName, curHHLoc, occupiedPositions);
                SetHHLocation(charName, nextLoc);
                if (nextLoc != "HarborHomeLivingRoom")
                {
                    occupiedPositions.Add(nextLoc);
                }

                hhNextRoamTime[charName] = Time.time + UnityEngine.Random.Range(15f, 45f);

                // Apply the HH location override
                SetCharacterLocation(charName, nextLoc);
                Debug.Log($"[HHRoam] {charName} roamed -> {nextLoc}");
            }
        }

        /// <summary>
        /// Picks a random HH room + position for a character, respecting:
        /// - Bathroom -> must go to Closet next
        /// - Pool -> must go to Bathroom next
        /// - Cannot share a room+position with another character
        /// Returns "HarborHomeLivingRoom" if no valid position is found.
        /// </summary>
        private static string PickRandomHHRoomPosition(string charName, string previousLocation, HashSet<string> occupiedPositions)
        {
            // Determine forced room based on previous location
            string forcedRoom = null;
            if (!string.IsNullOrEmpty(previousLocation))
            {
                string prevRoomName = GetRoomNameFromLocation(previousLocation);
                if (prevRoomName == "HarborHomeBathroom")
                    forcedRoom = "HarborHomeCloset";
                else if (prevRoomName == "HarborHomePool")
                    forcedRoom = "HarborHomeBathroom";
            }

            // Build list of candidate rooms
            string[] candidateRooms;
            if (forcedRoom != null)
            {
                candidateRooms = new string[] { forcedRoom };
            }
            else
            {
                candidateRooms = hhRoomNames;
            }

            // Build list of all valid room+position combinations
            List<string> validPositions = new List<string>();

            foreach (string roomName in candidateRooms)
            {
                GameObject roomLevel = GetHHRoomLevelGO(roomName);
                if (roomLevel == null)
                {
                    Debug.LogWarning($"[HHRoam] Room level GO not found for {roomName}");
                    continue;
                }

                Transform npcsParent = roomLevel.transform.Find("NPCs");
                if (npcsParent == null)
                {
                    Debug.LogWarning($"[HHRoam] NPCs child not found in room level {roomName}");
                    continue;
                }

                if (npcsParent.childCount == 0)
                {
                    Debug.LogWarning($"[HHRoam] No position children under NPCs in room {roomName}");
                    continue;
                }

                // Only use direct children as positions
                for (int i = 0; i < npcsParent.childCount; i++)
                {
                    Transform positionTransform = npcsParent.GetChild(i);
                    string positionName = roomName + positionTransform.name;

                    // Skip if already occupied by another character
                    if (occupiedPositions.Contains(positionName))
                        continue;

                    validPositions.Add(positionName);
                }
            }

            if (validPositions.Count == 0)
            {
                Debug.Log($"[HHRoam] No valid positions left for {charName}, defaulting to HarborHomeLivingRoom");
                return "HarborHomeLivingRoom";
            }

            // Pick a random valid position
            int randomIndex = UnityEngine.Random.Range(0, validPositions.Count);
            return validPositions[randomIndex];
        }

        /// <summary>
        /// Extracts the room name from a full location string (e.g. "HarborHomeBathroomShower" -> "HarborHomeBathroom").
        /// </summary>
        private static string GetRoomNameFromLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return null;

            foreach (string roomName in hhRoomNames)
            {
                if (location.StartsWith(roomName))
                    return roomName;
            }
            return location;
        }

        /// <summary>
        /// Returns the level GameObject for a given HH room name or full location string.
        /// </summary>
        private static GameObject GetHHRoomLevelGO(string location)
        {
            if (string.IsNullOrEmpty(location))
                return null;

            string roomName = GetRoomNameFromLocation(location);

            switch (roomName)
            {
                case "HarborHomeBathroom": return Places.harborHomeBathroomLevel;
                case "HarborHomeBedroom": return Places.harborHomeBedroomLevel;
                case "HarborHomeCloset": return Places.harborHomeClosetLevel;
                case "HarborHomeKitchen": return Places.harborHomeKitchenLevel;
                case "HarborHomeLivingRoom": return Places.harborHomeLivingroomLevel;
                case "HarborHomePool": return Places.harborHomePoolLevel;
                default: return null;
            }
        }

        /// <summary>
        /// Gets the HHLocation variable for a character by name.
        /// </summary>
        private static string GetHHLocation(string charName)
        {
            switch (charName)
            {
                case "Amber": return amberHHLocation;
                case "Claire": return claireHHLocation;
                case "Sarah": return sarahHHLocation;
                case "Anis": return anisHHLocation;
                case "Centi": return centiHHLocation;
                case "Dorothy": return dorothyHHLocation;
                case "Elegg": return eleggHHLocation;
                case "Frima": return frimaHHLocation;
                case "Guilty": return guiltyHHLocation;
                case "Helm": return helmHHLocation;
                case "Maiden": return maidenHHLocation;
                case "Mary": return maryHHLocation;
                case "Mast": return mastHHLocation;
                case "Neon": return neonHHLocation;
                case "Pepper": return pepperHHLocation;
                case "Rapi": return rapiHHLocation;
                case "Rosanna": return rosannaHHLocation;
                case "Sakura": return sakuraHHLocation;
                case "Tove": return toveHHLocation;
                case "Viper": return viperHHLocation;
                case "Yan": return yanHHLocation;
                default: return null;
            }
        }

        /// <summary>
        /// Sets the HHLocation variable for a character by name.
        /// </summary>
        private static void SetHHLocation(string charName, string location)
        {
            switch (charName)
            {
                case "Amber": amberHHLocation = location; break;
                case "Claire": claireHHLocation = location; break;
                case "Sarah": sarahHHLocation = location; break;
                case "Anis": anisHHLocation = location; break;
                case "Centi": centiHHLocation = location; break;
                case "Dorothy": dorothyHHLocation = location; break;
                case "Elegg": eleggHHLocation = location; break;
                case "Frima": frimaHHLocation = location; break;
                case "Guilty": guiltyHHLocation = location; break;
                case "Helm": helmHHLocation = location; break;
                case "Maiden": maidenHHLocation = location; break;
                case "Mary": maryHHLocation = location; break;
                case "Mast": mastHHLocation = location; break;
                case "Neon": neonHHLocation = location; break;
                case "Pepper": pepperHHLocation = location; break;
                case "Rapi": rapiHHLocation = location; break;
                case "Rosanna": rosannaHHLocation = location; break;
                case "Sakura": sakuraHHLocation = location; break;
                case "Tove": toveHHLocation = location; break;
                case "Viper": viperHHLocation = location; break;
                case "Yan": yanHHLocation = location; break;
            }
        }

        /// <summary>
        /// Gets the default location variable for a character by name.
        /// </summary>
        private static string GetDefaultLocation(string charName)
        {
            switch (charName)
            {
                case "Amber": return amberDefaultLocation;
                case "Claire": return claireDefaultLocation;
                case "Sarah": return sarahDefaultLocation;
                case "Anis": return anisDefaultLocation;
                case "Centi": return centiDefaultLocation;
                case "Dorothy": return dorothyDefaultLocation;
                case "Elegg": return eleggDefaultLocation;
                case "Frima": return frimaDefaultLocation;
                case "Guilty": return guiltyDefaultLocation;
                case "Helm": return helmDefaultLocation;
                case "Maiden": return maidenDefaultLocation;
                case "Mary": return maryDefaultLocation;
                case "Mast": return mastDefaultLocation;
                case "Neon": return neonDefaultLocation;
                case "Pepper": return pepperDefaultLocation;
                case "Rapi": return rapiDefaultLocation;
                case "Rosanna": return rosannaDefaultLocation;
                case "Sakura": return sakuraDefaultLocation;
                case "Tove": return toveDefaultLocation;
                case "Viper": return viperDefaultLocation;
                case "Yan": return yanDefaultLocation;
                default: return "Unknown";
            }
        }

        #endregion

        private static void SetDay1Schedule()
        {
            // All characters in their rooms - only show if their condition is met
            amberLocation = SaveManager.GetBool("SecretBeach_UnlockedLab") ? ((MainStory.generalLotteryNumber1 <= 60) ? "Hospitalhallway" : amberDefaultLocation) : "Unknown";
            claireLocation = (SaveManager.GetInt("GiftShop_BuildCounter") >= 2) ? claireDefaultLocation : "Unknown";
            sarahLocation = "Unknown";
            anisLocation = SaveManager.GetBool("Voyeur_SeenAnis") ? anisDefaultLocation : "Unknown";
            centiLocation = SaveManager.GetBool("Voyeur_SeenCenti") ? centiDefaultLocation : "Unknown";
            dorothyLocation = SaveManager.GetBool("Voyeur_SeenDorothy") ? dorothyDefaultLocation : "Unknown";
            eleggLocation = SaveManager.GetBool("Voyeur_SeenElegg") ? eleggDefaultLocation : "Unknown";
            frimaLocation = SaveManager.GetBool("Voyeur_SeenFrima") ? ((MainStory.generalLotteryNumber2 <= 70) ? "Hotel" : frimaDefaultLocation) : "Unknown";
            guiltyLocation = SaveManager.GetBool("Voyeur_SeenGuilty") ? guiltyDefaultLocation : "Unknown";
            helmLocation = SaveManager.GetBool("Voyeur_SeenHelm") ? helmDefaultLocation : "Unknown";
            maidenLocation = SaveManager.GetBool("Voyeur_SeenMaiden") ? maidenDefaultLocation : "Unknown";
            maryLocation = SaveManager.GetBool("Voyeur_SeenMary") ? maryDefaultLocation : "Unknown";
            mastLocation = SaveManager.GetBool("Voyeur_SeenMast") ? mastDefaultLocation : "Unknown";
            neonLocation = SaveManager.GetBool("Voyeur_SeenNeon") ? ((MainStory.generalLotteryNumber3 <= 70 && !Places.GetBadWeather()) ? "Temple" : neonDefaultLocation) : "Unknown";
            pepperLocation = SaveManager.GetBool("Voyeur_SeenPepper") ? pepperDefaultLocation : "Unknown";
            rapiLocation = SaveManager.GetBool("Voyeur_SeenRapi") ? rapiDefaultLocation : "Unknown";
            rosannaLocation = SaveManager.GetBool("Voyeur_SeenRosanna") ? rosannaDefaultLocation : "Unknown";
            sakuraLocation = SaveManager.GetBool("Voyeur_SeenSakura") ? sakuraDefaultLocation : "Unknown";
            toveLocation = SaveManager.GetBool("Voyeur_SeenTove") ? toveDefaultLocation : "Unknown";
            viperLocation = SaveManager.GetBool("Voyeur_SeenViper") ? ((MainStory.generalLotteryNumber1 >= 30 && !Places.GetBadWeather()) ? "Villa" : viperDefaultLocation) : "Unknown";
            yanLocation = SaveManager.GetBool("Voyeur_SeenYan") ? yanDefaultLocation : "Unknown";

            snekLocation = "Unknown";
        }

        private static void SetDay2Schedule()
        {
            // All characters in their rooms - only show if their condition is met
            amberLocation = SaveManager.GetBool("SecretBeach_UnlockedLab") ? amberDefaultLocation : "Unknown";
            claireLocation = (SaveManager.GetInt("GiftShop_BuildCounter") >= 2) ? claireDefaultLocation : "Unknown";
            sarahLocation = "Unknown";
            anisLocation = SaveManager.GetBool("Voyeur_SeenAnis") ? ((SaveManager.GetBool("Affection_Anis_Seen1") && !SaveManager.GetBool("Affection_Anis_Seen2") && SaveManager.GetInt("Affection_Anis") >= 4) ? 
                "Mall" : (MainStory.generalLotteryNumber3 <= 70) ? "Mall" : anisDefaultLocation) : "Unknown";
            centiLocation = SaveManager.GetBool("Voyeur_SeenCenti") ? centiDefaultLocation : "Unknown";
            dorothyLocation = SaveManager.GetBool("Voyeur_SeenDorothy") ? dorothyDefaultLocation : "Unknown";
            eleggLocation = SaveManager.GetBool("Voyeur_SeenElegg") ? eleggDefaultLocation : "Unknown";
            frimaLocation = SaveManager.GetBool("Voyeur_SeenFrima") ? frimaDefaultLocation : "Unknown";
            guiltyLocation = SaveManager.GetBool("Voyeur_SeenGuilty") ? ((MainStory.generalLotteryNumber1 <= 70 && !Places.GetBadWeather()) ? "Parkinglot" : guiltyDefaultLocation) : "Unknown";
            helmLocation = SaveManager.GetBool("Voyeur_SeenHelm") ? helmDefaultLocation : "Unknown";
            maidenLocation = SaveManager.GetBool("Voyeur_SeenMaiden") ? maidenDefaultLocation : "Unknown";
            maryLocation = SaveManager.GetBool("Voyeur_SeenMary") ? maryDefaultLocation : "Unknown";
            mastLocation = SaveManager.GetBool("Voyeur_SeenMast") ? mastDefaultLocation : "Unknown";
            neonLocation = SaveManager.GetBool("Voyeur_SeenNeon") ? neonDefaultLocation : "Unknown";
            pepperLocation = SaveManager.GetBool("Voyeur_SeenPepper") ? ((MainStory.generalLotteryNumber2 <= 70) ? "Hospital" : pepperDefaultLocation) : "Unknown";
            rapiLocation = SaveManager.GetBool("Voyeur_SeenRapi") ? rapiDefaultLocation : "Unknown";
            rosannaLocation = SaveManager.GetBool("Voyeur_SeenRosanna") ? rosannaDefaultLocation : "Unknown";
            sakuraLocation = SaveManager.GetBool("Voyeur_SeenSakura") ? sakuraDefaultLocation : "Unknown";
            toveLocation = SaveManager.GetBool("Voyeur_SeenTove") ? ((MainStory.generalLotteryNumber1 >= 30 && !Places.GetBadWeather()) ? "Trail" : toveDefaultLocation) : "Unknown";
            viperLocation = SaveManager.GetBool("Voyeur_SeenViper") ? viperDefaultLocation : "Unknown";
            yanLocation = SaveManager.GetBool("Voyeur_SeenYan") ? yanDefaultLocation : "Unknown";

            snekLocation = "Unknown";
        }

        private static void SetDay3Schedule()
        {
            // All characters in their rooms - only show if their condition is met
            amberLocation = SaveManager.GetBool("SecretBeach_UnlockedLab") ? amberDefaultLocation : "Unknown";
            claireLocation = (SaveManager.GetInt("GiftShop_BuildCounter") >= 2) ? claireDefaultLocation : "Unknown";
            sarahLocation = "Unknown";
            anisLocation = SaveManager.GetBool("Voyeur_SeenAnis") ? anisDefaultLocation : "Unknown";
            centiLocation = SaveManager.GetBool("Voyeur_SeenCenti") ? ((MainStory.generalLotteryNumber1 <= 70 && !Places.GetBadWeather() && Core.GetVariableBool("samantha-met")) ? "Kenshome" : centiDefaultLocation) : "Unknown";
            dorothyLocation = SaveManager.GetBool("Voyeur_SeenDorothy") ? dorothyDefaultLocation : "Unknown";
            eleggLocation = SaveManager.GetBool("Voyeur_SeenElegg") ? eleggDefaultLocation : "Unknown";
            frimaLocation = SaveManager.GetBool("Voyeur_SeenFrima") ? frimaDefaultLocation : "Unknown";
            guiltyLocation = SaveManager.GetBool("Voyeur_SeenGuilty") ? guiltyDefaultLocation : "Unknown";
            helmLocation = SaveManager.GetBool("Voyeur_SeenHelm") ? helmDefaultLocation : "Unknown";
            maidenLocation = SaveManager.GetBool("Voyeur_SeenMaiden") ? maidenDefaultLocation : "Unknown";
            maryLocation = SaveManager.GetBool("Voyeur_SeenMary") ? ((MainStory.generalLotteryNumber1 >= 30) ? "Hospitalhallway" : maryDefaultLocation) : "Unknown";
            mastLocation = SaveManager.GetBool("Voyeur_SeenMast") ? ((MainStory.generalLotteryNumber2 <= 70 && !Places.GetBadWeather()) ? "Beach" : mastDefaultLocation) : "Unknown";
            neonLocation = SaveManager.GetBool("Voyeur_SeenNeon") ? neonDefaultLocation : "Unknown";
            pepperLocation = SaveManager.GetBool("Voyeur_SeenPepper") ? pepperDefaultLocation : "Unknown";
            rapiLocation = SaveManager.GetBool("Voyeur_SeenRapi") ? ((MainStory.generalLotteryNumber3 <= 70) ? "Gasstation" : rapiDefaultLocation) : "Unknown";
            rosannaLocation = SaveManager.GetBool("Voyeur_SeenRosanna") ? rosannaDefaultLocation : "Unknown";
            sakuraLocation = SaveManager.GetBool("Voyeur_SeenSakura") ? sakuraDefaultLocation : "Unknown";
            toveLocation = SaveManager.GetBool("Voyeur_SeenTove") ? toveDefaultLocation : "Unknown";
            viperLocation = SaveManager.GetBool("Voyeur_SeenViper") ? viperDefaultLocation : "Unknown";
            yanLocation = SaveManager.GetBool("Voyeur_SeenYan") ? yanDefaultLocation : "Unknown";

            snekLocation = (MainStory.generalLotteryNumber3 == 69) ? "Forest" : "Unknown";
        }

        private static void SetDay4Schedule()
        {
            // All characters in their rooms - only show if their condition is met
            amberLocation = SaveManager.GetBool("SecretBeach_UnlockedLab") ? amberDefaultLocation : "Unknown";
            claireLocation = "Unknown";
            sarahLocation = "Unknown";
            anisLocation = SaveManager.GetBool("Voyeur_SeenAnis") ? ((!SaveManager.GetBool("Affection_Anis_Seen1") && SaveManager.GetInt("Affection_Anis") >= 2) ? 
                ((!Places.GetBadWeather()) ? "Downtown" : anisDefaultLocation) : ((MainStory.generalLotteryNumber2 >= 30 && !Places.GetBadWeather()) ? "Downtown" : anisDefaultLocation)) : "Unknown";
            centiLocation = SaveManager.GetBool("Voyeur_SeenCenti") ? centiDefaultLocation : "Unknown";
            dorothyLocation = SaveManager.GetBool("Voyeur_SeenDorothy") ? ((MainStory.generalLotteryNumber2 <= 70 && !Places.GetBadWeather()) ? "Park" : dorothyDefaultLocation) : "Unknown";
            eleggLocation = SaveManager.GetBool("Voyeur_SeenElegg") ? eleggDefaultLocation : "Unknown";
            frimaLocation = SaveManager.GetBool("Voyeur_SeenFrima") ? frimaDefaultLocation : "Unknown";
            guiltyLocation = SaveManager.GetBool("Voyeur_SeenGuilty") ? guiltyDefaultLocation : "Unknown";
            helmLocation = SaveManager.GetBool("Voyeur_SeenHelm") ? ((MainStory.generalLotteryNumber1 <= 70 && !Places.GetBadWeather()) ? "Beach" : helmDefaultLocation) : "Unknown";
            maidenLocation = SaveManager.GetBool("Voyeur_SeenMaiden") ? maidenDefaultLocation : "Unknown";
            maryLocation = SaveManager.GetBool("Voyeur_SeenMary") ? maryDefaultLocation : "Unknown";
            mastLocation = SaveManager.GetBool("Voyeur_SeenMast") ? mastDefaultLocation : "Unknown";
            neonLocation = SaveManager.GetBool("Voyeur_SeenNeon") ? neonDefaultLocation : "Unknown";
            pepperLocation = SaveManager.GetBool("Voyeur_SeenPepper") ? pepperDefaultLocation : "Unknown";
            rapiLocation = SaveManager.GetBool("Voyeur_SeenRapi") ? rapiDefaultLocation : "Unknown";
            rosannaLocation = SaveManager.GetBool("Voyeur_SeenRosanna") ? ((MainStory.generalLotteryNumber3 <= 70 && !Places.GetBadWeather()) ? "Gabrielsmansion" : rosannaDefaultLocation) : "Unknown";
            sakuraLocation = SaveManager.GetBool("Voyeur_SeenSakura") ? sakuraDefaultLocation : "Unknown";
            toveLocation = SaveManager.GetBool("Voyeur_SeenTove") ? toveDefaultLocation : "Unknown";
            viperLocation = SaveManager.GetBool("Voyeur_SeenViper") ? viperDefaultLocation : "Unknown";
            yanLocation = SaveManager.GetBool("Voyeur_SeenYan") ? ((MainStory.generalLotteryNumber1 >= 30) ? "Mall" : yanDefaultLocation) : "Unknown";

            snekLocation = "Unknown";
        }

        private static void SetDay5Schedule()
        {
            // All characters in their rooms - only show if their condition is met
            amberLocation = SaveManager.GetBool("SecretBeach_UnlockedLab") ? amberDefaultLocation : "Unknown";
            claireLocation = (SaveManager.GetInt("GiftShop_BuildCounter") >= 2) ? claireDefaultLocation : "Unknown";
            sarahLocation = "Unknown";
            anisLocation = SaveManager.GetBool("Voyeur_SeenAnis") ? ((SaveManager.GetBool("Affection_Anis_Seen2") && !SaveManager.GetBool("Affection_Anis_Seen3") && SaveManager.GetInt("Affection_Anis") >= 5) ?
                ((!Places.GetBadWeather()) ? "SecretBeach" : anisDefaultLocation) : ((MainStory.generalLotteryNumber1 >= 30 && !Places.GetBadWeather()) ? "SecretBeach" : anisDefaultLocation)) : "Unknown";
            centiLocation = SaveManager.GetBool("Voyeur_SeenCenti") ? centiDefaultLocation : "Unknown";
            dorothyLocation = SaveManager.GetBool("Voyeur_SeenDorothy") ? dorothyDefaultLocation : "Unknown";
            eleggLocation = SaveManager.GetBool("Voyeur_SeenElegg") ? ((MainStory.generalLotteryNumber2 <= 70 && !Places.GetBadWeather()) ? "Downtown" : eleggDefaultLocation) : "Unknown";
            frimaLocation = SaveManager.GetBool("Voyeur_SeenFrima") ? frimaDefaultLocation : "Unknown";
            guiltyLocation = SaveManager.GetBool("Voyeur_SeenGuilty") ? guiltyDefaultLocation : "Unknown";
            helmLocation = SaveManager.GetBool("Voyeur_SeenHelm") ? helmDefaultLocation : "Unknown";
            maidenLocation = SaveManager.GetBool("Voyeur_SeenMaiden") ? ((MainStory.generalLotteryNumber1 <= 70 && !Places.GetBadWeather()) ? "Alley" : maidenDefaultLocation) : "Unknown";
            maryLocation = SaveManager.GetBool("Voyeur_SeenMary") ? maryDefaultLocation : "Unknown";
            mastLocation = SaveManager.GetBool("Voyeur_SeenMast") ? mastDefaultLocation : "Unknown";
            neonLocation = SaveManager.GetBool("Voyeur_SeenNeon") ? neonDefaultLocation : "Unknown";
            pepperLocation = SaveManager.GetBool("Voyeur_SeenPepper") ? pepperDefaultLocation : "Unknown";
            rapiLocation = SaveManager.GetBool("Voyeur_SeenRapi") ? rapiDefaultLocation : "Unknown";
            rosannaLocation = SaveManager.GetBool("Voyeur_SeenRosanna") ? rosannaDefaultLocation : "Unknown";
            sakuraLocation = SaveManager.GetBool("Voyeur_SeenSakura") ? ((MainStory.generalLotteryNumber3 <= 70 && !Places.GetBadWeather()) ? "Forest" : sakuraDefaultLocation) : "Unknown";
            toveLocation = SaveManager.GetBool("Voyeur_SeenTove") ? toveDefaultLocation : "Unknown";
            viperLocation = SaveManager.GetBool("Voyeur_SeenViper") ? viperDefaultLocation : "Unknown";
            yanLocation = SaveManager.GetBool("Voyeur_SeenYan") ? yanDefaultLocation : "Unknown";

            snekLocation = "Unknown";
        }

        // Public method to get a character's current location
        public static string GetCharacterLocation(string characterName)
        {
            switch (characterName.ToLower())
            {
                case "amber": return amberLocation;
                case "claire": return claireLocation;
                case "sarah": return sarahLocation;
                case "anis": return anisLocation;
                case "centi": return centiLocation;
                case "dorothy": return dorothyLocation;
                case "elegg": return eleggLocation;
                case "frima": return frimaLocation;
                case "guilty": return guiltyLocation;
                case "helm": return helmLocation;
                case "maiden": return maidenLocation;
                case "mary": return maryLocation;
                case "mast": return mastLocation;
                case "neon": return neonLocation;
                case "pepper": return pepperLocation;
                case "rapi": return rapiLocation;
                case "rosanna": return rosannaLocation;
                case "sakura": return sakuraLocation;
                case "tove": return toveLocation;
                case "viper": return viperLocation;
                case "yan": return yanLocation;
                case "snek": return snekLocation;
                default: return null;
            }
        }

        // Public method to check if any character's location starts with the given prefix
        public static bool AnyCharacterLocationStartsWith(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return false;

            return amberLocation != null && amberLocation.StartsWith(prefix) ||
                   claireLocation != null && claireLocation.StartsWith(prefix) ||
                   sarahLocation != null && sarahLocation.StartsWith(prefix) ||
                   anisLocation != null && anisLocation.StartsWith(prefix) ||
                   centiLocation != null && centiLocation.StartsWith(prefix) ||
                   dorothyLocation != null && dorothyLocation.StartsWith(prefix) ||
                   eleggLocation != null && eleggLocation.StartsWith(prefix) ||
                   frimaLocation != null && frimaLocation.StartsWith(prefix) ||
                   guiltyLocation != null && guiltyLocation.StartsWith(prefix) ||
                   helmLocation != null && helmLocation.StartsWith(prefix) ||
                   maidenLocation != null && maidenLocation.StartsWith(prefix) ||
                   maryLocation != null && maryLocation.StartsWith(prefix) ||
                   mastLocation != null && mastLocation.StartsWith(prefix) ||
                   neonLocation != null && neonLocation.StartsWith(prefix) ||
                   pepperLocation != null && pepperLocation.StartsWith(prefix) ||
                   rapiLocation != null && rapiLocation.StartsWith(prefix) ||
                   rosannaLocation != null && rosannaLocation.StartsWith(prefix) ||
                   sakuraLocation != null && sakuraLocation.StartsWith(prefix) ||
                   toveLocation != null && toveLocation.StartsWith(prefix) ||
                   viperLocation != null && viperLocation.StartsWith(prefix) ||
                   yanLocation != null && yanLocation.StartsWith(prefix) ||
                   snekLocation != null && snekLocation.StartsWith(prefix);
        }

        // Returns all character locations dynamically via reflection — picks up new characters automatically
        private static Dictionary<string, string> GetAllCharacterLocations()
        {
            var result = new Dictionary<string, string>();
            var fields = typeof(Schedule).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.FieldType != typeof(string)) continue;
                string name = field.Name;
                if (!name.EndsWith("Location")) continue;
                if (name.EndsWith("DefaultLocation") || name.EndsWith("HHLocation")) continue;

                string charName = name.Substring(0, name.Length - "Location".Length);
                string location = field.GetValue(null) as string;
                if (!string.IsNullOrEmpty(charName))
                    result[charName] = location;
            }
            return result;
        }

        // Checks if any character is currently at the given location (exact match)
        public static bool IsAnyoneAtLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) return false;
            foreach (var kvp in GetAllCharacterLocations())
            {
                if (kvp.Value != null && kvp.Value == location)
                    return true;
            }
            return false;
        }

        // Checks if any character besides the specified ones is at the given location (exact match)
        public static bool IsAnyoneAtLocationBesides(string location, string exclude1 = null, string exclude2 = null, string exclude3 = null, string exclude4 = null, string exclude5 = null)
        {
            if (string.IsNullOrEmpty(location)) return false;
            foreach (var kvp in GetAllCharacterLocations())
            {
                if (kvp.Value == null || kvp.Value != location) continue;
                string capitalized = char.ToUpper(kvp.Key[0]) + kvp.Key.Substring(1);
                if (capitalized == exclude1 || capitalized == exclude2 || capitalized == exclude3 || capitalized == exclude4 || capitalized == exclude5) continue;
                return true;
            }
            return false;
        }

        // Returns a list of character names currently at the given location (exact match)
        public static List<string> GetCharactersAtLocation(string location)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(location)) return result;
            foreach (var kvp in GetAllCharacterLocations())
            {
                if (kvp.Value != null && kvp.Value == location)
                    result.Add(char.ToUpper(kvp.Key[0]) + kvp.Key.Substring(1));
            }
            return result;
        }

        // Public method to manually set a character's location
        public static void SetCharacterLocation(string characterName, string location)
        {
            switch (characterName.ToLower())
            {
                case "amber": amberLocation = location; break;
                case "claire": claireLocation = location; break;
                case "sarah": sarahLocation = location; break;
                case "anis": anisLocation = location; break;
                case "centi": centiLocation = location; break;
                case "dorothy": dorothyLocation = location; break;
                case "elegg": eleggLocation = location; break;
                case "frima": frimaLocation = location; break;
                case "guilty": guiltyLocation = location; break;
                case "helm": helmLocation = location; break;
                case "maiden": maidenLocation = location; break;
                case "mary": maryLocation = location; break;
                case "mast": mastLocation = location; break;
                case "neon": neonLocation = location; break;
                case "pepper": pepperLocation = location; break;
                case "rapi": rapiLocation = location; break;
                case "rosanna": rosannaLocation = location; break;
                case "sakura": sakuraLocation = location; break;
                case "tove": toveLocation = location; break;
                case "viper": viperLocation = location; break;
                case "yan": yanLocation = location; break;
                case "snek": snekLocation = location; break;
            }
            
            // Signal that schedule was updated
            scheduleVersion++;
        }

        private IEnumerator UpdateScheduleWithDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            UpdateScheduleForDay();
        }
    }
}
