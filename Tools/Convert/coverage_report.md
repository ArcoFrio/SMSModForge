# Conversion coverage report

Dialogues converted: **80**  |  total flags: **173**

Every item below was emitted with a `TODO`/placeholder or skipped — review each in the editor.

## condition (1)
- **SBDialogueMainGatekeeper**: ConditionGameObjectActive marker check -> review

## conditional (33)
- **AnisDialogueAffection01**: branch-dependent op dropped: if(Scenes.anisAffection01Scene01.activeSelf) -> place manually
- **AnisDialogueAffection01**: branch-dependent op dropped: if(Scenes.anisAffection01Scene01.activeSelf) -> place manually
- **AnisDialogueAffection02**: branch-dependent op dropped: if(this.dialogueToActivate.transform.Find("MouthActivator").gam) -> place manually
- **AnisDialogueAffection02**: branch-dependent op dropped: if(this.dialogueToActivate.transform.Find("MouthActivator").gam) -> place manually
- **AnisDialogueAffection03**: branch-dependent op dropped: if(Scenes.anisAffection03Scene05.activeSelf) -> place manually
- **AnisDialogueAffection03**: branch-dependent op dropped: if(Scenes.anisAffection03Scene05.activeSelf) -> place manually
- **AnisDialogueHHDefault**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefault**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefault**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefault**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefault**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefault**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisDialogueHHDefaultMovie**: branch-dependent op dropped: if(!Dialogues.giftUI.activeSelf) -> place manually
- **AnisRandomChill01**: branch-dependent op dropped: if(!SaveManager.GetBool("Affection_Anis_Seen2")) -> place manually
- **AnisRandomChill01**: branch-dependent op dropped: if(!SaveManager.GetBool("Affection_Anis_Seen2")) -> place manually
- **AnisRandomChill01**: branch-dependent op dropped: if(!SaveManager.GetBool("Affection_Anis_Seen2")) -> place manually
- **SarahDialogueBuyHH**: branch-dependent op dropped: if(!SaveManager.GetBool("HarborHome_FirstVisited")) -> place manually
- **SarahDialogueBuyHH**: branch-dependent op dropped: if(!SaveManager.GetBool("HarborHome_FirstVisited")) -> place manually
- **SarahDialogueBuyHH**: branch-dependent op dropped: if(!SaveManager.GetBool("HarborHome_FirstVisited")) -> place manually
- **SarahDialogueBuyHH**: branch-dependent op dropped: if(!SaveManager.GetBool("HarborHome_FirstVisited")) -> place manually
- **SBDialogueMain**: branch-dependent op dropped: if(SaveManager.GetInt("SecretBeach_RelaxedAmount") > 2 && voyeu) -> place manually
- **SBDialogueMain**: branch-dependent op dropped: if(SaveManager.GetInt("SecretBeach_RelaxedAmount") > 2 && voyeu) -> place manually
- **SBDialogueMainGatekeeper**: branch-dependent op dropped: if(Places.secretBeachLevel.transform.position.y > -17) -> place manually
- **SBDialogueMainGatekeeper**: branch-dependent op dropped: if(Places.secretBeachLevel.transform.position.y < 0) -> place manually

## mouth (1)
- **AnisDialogueAffection02**: MouthActivator toggle -> review (talk-anim / branch flag)

## null-target (2)
- **AnisRandomShower01**: SetActive with null target (node actor anis)
- **DorothyDialogueSecretbeach01**: SetActive with null target (node actor dorothy)

## raw-op (118)
- **AmberDialogueDefault**: amberDefaultDiagQueued = false
- **AnisDialogueDefault**: Dialogues.giftUI.SetActive(true)
- **AnisDialogueDefault**: Core.FindAndModifyProxyVariableString("Gifting_Target", "Anis")
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Schedule.anisHHOutfit.SetActive(false)
- **AnisDialogueGift**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Schedule.anisHHOutfit.SetActive(false)
- **AnisDialogueGift**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Schedule.anisHHOutfit.SetActive(false)
- **AnisDialogueGift**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Schedule.anisHHOutfit.SetActive(false)
- **AnisDialogueGift**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Core.affectionIncrease.SetActive(true)
- **AnisDialogueGift**: Schedule.anisHHOutfit.SetActive(false)
- **AnisDialogueGift**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueGift**: Schedule.anisHHOutfit.SetActive(false)
- **AnisDialogueGift**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefault**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefault**: Dialogues.giftUI.SetActive(true)
- **AnisDialogueHHDefault**: Core.FindAndModifyProxyVariableString("Gifting_Target", "Anis")
- **AnisDialogueHHDefault**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefault**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefault**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefault**: Dialogues.UpdateHHTalkPanel(true)
- **AnisDialogueHHDefault**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisDialogueHHDefault**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefault**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Dialogues.giftUI.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Core.FindAndModifyProxyVariableString("Gifting_Target", "Anis")
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Dialogues.UpdateHHTalkPanel(true)
- **AnisDialogueHHDefaultMovie**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Core.FindAndModifyVariableBool("watching-porn",false)
- **AnisDialogueHHDefaultMovie**: //Core.FindAndModifyVariableDouble("random-1-of-10", 0)
- **AnisDialogueHHDefaultMovie**: Core.FindAndModifyVariableDouble("incoming-movie", 0)
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Core.FindAndModifyVariableDouble("incoming-movie", 1)
- **AnisDialogueHHDefaultMovie**: StartMovieSequence()
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: Core.FindAndModifyVariableDouble("incoming-movie", 3)
- **AnisDialogueHHDefaultMovie**: Core.FindAndModifyVariableDouble("incoming-movie", 2)
- **AnisDialogueHHDefaultMovie**: StartMovieSequence()
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultMovie**: StartMovieSequence()
- **AnisDialogueHHDefaultMovie**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultPool**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisDialogueHHDefaultPool**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisDialogueHHDefaultPool**: Core.FindAndModifyProxyVariableString("Minigame_Massage_Character", "Anis")
- **AnisDialogueHHDefaultPool**: Minigames.Instance.StartMinigame(Minigames.minigameMassage)
- **AnisDialogueHHDefaultPool**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisDialogueHHDefaultPool**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisRandomMovie01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomMovie01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomMovie01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomMovie01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomMovie01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomMovie01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomShower01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomShower01**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisRandomShower01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomShower01**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisRandomShower01**: Schedule.anisHHOutfit.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true)
- **AnisRandomShower01**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisRandomSleep01**: Core.afterSleepEvents.GetComponent<CanvasGroup>().alpha = 1
- **AnisRandomSleep01**: Core.afterSleepEvents.GetComponent<CanvasGroup>().interactable = true
- **AnisRandomSleep01**: //Characters.anisCoatless.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true
- **AnisRandomSleep01**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisRandomSleep01**: Core.afterSleepEvents.GetComponent<CanvasGroup>().alpha = 1
- **AnisRandomSleep01**: Core.afterSleepEvents.GetComponent<CanvasGroup>().interactable = true
- **AnisRandomSleep01**: //Characters.anisCoatless.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true
- **AnisRandomSleep01**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **AnisRandomSleep01**: Core.afterSleepEvents.GetComponent<CanvasGroup>().alpha = 1
- **AnisRandomSleep01**: Core.afterSleepEvents.GetComponent<CanvasGroup>().interactable = true
- **AnisRandomSleep01**: //Characters.anisCoatless.transform.Find("MBase1").Find("Leave").gameObject.SetActive(true
- **AnisRandomSleep01**: Places.harborHomeBedroomButtonCanvas.SetActive(true)
- **ClaireDialogueDefault**: Places.ActivateShop(Places.giftStore)
- **SarahDialogueBuyHH**: Core.FindAndModifyVariableDouble("Cash", Core.GetVariableNumber("Cash") - 5000000)
- **SarahDialogueBuyHH**: Core.FindAndModifyVariableDouble("Cash", Core.GetVariableNumber("Cash") - 5000000)
- **SnekForest**: Color c = Places.solid.GetComponent<SpriteRenderer>().color
- **SnekForest**: c.a = Mathf.MoveTowards(c.a, 1f, 1f * Time.deltaTime)
- **SnekForest**: Places.solid.GetComponent<SpriteRenderer>().color = c
- **SnekForest**: snekIsSolid = true
- **SnekForest**: snekIsSolid = false
- **GSDialogueStory05**: Core.affectionIncrease.SetActive(true)
- **MLDialogueStory02**: Core.affectionIncrease.SetActive(true)
- **MLDialogueStory04**: Core.affectionIncrease.SetActive(true)
- **SBDialogueMain**: relaxed = true
- **SBDialogueMain**: relaxed = true
- **SBDialogueMainFirst**: actionTodaySB = true
- **SBDialogueMainGatekeeper**: Places.secretBeachLevel.GetComponent<ParallaxMouseEffect>().enabled = false
- **SBDialogueMainGatekeeper**: Places.secretBeachLevelBG.GetComponent<ParallaxMouseEffect>().enabled = false
- **SBDialogueMainGatekeeper**: Places.secretBeachLevel.transform.position = new Vector2(Places.secretBeachLevel.transform
- **SBDialogueMainGatekeeper**: Places.secretBeachGatekeeperB.SetActive(true)
- **SBDialogueMainGatekeeper**: Places.secretBeachFlash.SetActive(true)
- **SBDialogueMainGatekeeper**: Places.secretBeachGatekeeper.SetActive(false)
- **SBDialogueMainGatekeeper**: Places.secretBeachGatekeeperB.SetActive(false)
- **SBDialogueMainGatekeeper**: Places.secretBeachLevel.transform.position = new Vector2(Places.secretBeachLevel.transform
- **SBDialogueMainGatekeeper**: Places.secretBeachLevel.GetComponent<ParallaxMouseEffect>().enabled = true
- **SBDialogueMainGatekeeper**: Places.secretBeachLevelBG.GetComponent<ParallaxMouseEffect>().enabled = true
- **SBDialogueMainGatekeeper**: actionTodaySB = true
- **SBDialogueMainGatekeeper**: Places.secretBeachLevel.transform.position = new Vector2(Places.secretBeachLevel.transform
- **SBDialogueMainGatekeeper**: Places.secretBeachLevel.GetComponent<ParallaxMouseEffect>().enabled = true
- **SBDialogueMainGatekeeper**: Places.secretBeachLevelBG.GetComponent<ParallaxMouseEffect>().enabled = true
- **SBDialogueMainGatekeeper**: actionTodaySB = true
- **SBDialogueStory01**: actionTodaySB = true

## roomtalk (12)
- **CentiDialogueKenshome01**: couldn't derive roomTalk from level 'vanilla:21_Suburban Exterior House' — set manually
- **DorothyDialoguePark01**: couldn't derive roomTalk from level 'vanilla:58_Subpark' — set manually
- **FrimaDialogueHotel01**: couldn't derive roomTalk from level 'vanilla:39_HotelLobby' — set manually
- **GuiltyDialogueParkinglot01**: couldn't derive roomTalk from level 'vanilla:110_BadlandsParkingLot' — set manually
- **MaidenDialogueAlley01**: couldn't derive roomTalk from level 'vanilla:111_BadlandsParkingLotBackside' — set manually
- **NeonDialogueTemple01**: couldn't derive roomTalk from level 'vanilla:68_Jap_Temple' — set manually
- **PepperDialogueHospital01**: couldn't derive roomTalk from level 'vanilla:84_HospitalEntrance' — set manually
- **RosannaDialogueGabrielsmansion01**: couldn't derive roomTalk from level 'vanilla:35_MansionOutside' — set manually
- **SakuraDialogueForest01**: couldn't derive roomTalk from level 'vanilla:67_Jap_ForestEntrance' — set manually
- **ToveDialogueTrail01**: couldn't derive roomTalk from level 'vanilla:138_HikingPath_Start' — set manually
- **ViperDialogueVilla01**: couldn't derive roomTalk from level 'vanilla:70_Villa_Outside' — set manually
- **SnekForest**: couldn't derive roomTalk from level 'vanilla:67_Jap_ForestEntrance' — set manually

## sfx (3)
- **SarahDialogueBuyHH**: audio clip 'Cash Register' has no SFX key
- **SarahDialogueBuyHH**: audio clip 'Cash Register' has no SFX key
- **SBDialogueMainGatekeeper**: audio clip 'PortalSound' has no SFX key

## transition (2)
- **AnisDialogueAffection02**: TransitionLevels levelMall->levelCinema needs level tokens
- **AnisDialogueAffection02**: TransitionLevels levelCinema->levelMall needs level tokens

## variable (1)
- **SarahDialogueBuyHH**: condition variable 'Gameplay_Cash' not declared in pack
