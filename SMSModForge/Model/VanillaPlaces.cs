using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Catalog of vanilla <c>5_Levels</c> children in Starmaker Story 1.8E.
/// Used by the editor's navigator-target picker so users can wire a custom
/// place's button to a vanilla destination by name. The token on the wire
/// is <c>"vanilla:&lt;goName&gt;"</c> — the GO name is the only stable
/// handle, since sibling indices are unstable once mods add levels.
/// <para/>
/// Derived from <c>a decompiled scene-hierarchy dump of the target game</c>.
/// Update this list when the target game version's level set changes.
/// </summary>
public static class VanillaPlaces
{
    /// <summary>
    /// One vanilla level. <see cref="RoomTalkName"/> is the corresponding child
    /// under <c>8_Room_Talk</c> when known — vanilla pairings are name-based
    /// rather than positional, so a few places have hand-mapped roomtalks
    /// (<c>5_MyRoom</c> → <c>My_Room</c>) and a handful have no roomtalk at all
    /// (intro/ending levels, sub-locations of larger areas).
    /// </summary>
    public sealed record VanillaPlace(string GoName, string DisplayName, string? RoomTalkName = null);

    /// <summary>
    /// Every vanilla level GameObject under <c>5_Levels</c>. Order matches
    /// scene sibling order. Two entries (<c>21_Suburban Exterior House</c>,
    /// <c>24_Suburban Living Room</c>) contain whitespace in the GO name —
    /// they round-trip through the manifest unchanged.
    /// </summary>
    public static readonly IReadOnlyList<VanillaPlace> All = new VanillaPlace[]
    {
        new("0_None",                          "None"),
        new("1_Master_Bedrooom",               "Master Bedroom",                 "masterbedroom"),
        new("2_Kitchen",                       "Kitchen",                        "Kitchen"),
        new("3_LivingRoom",                    "Living Room",                    "Livingroom"),
        new("4_Bath",                          "Bath",                           "Bath"),
        new("5_MyRoom",                        "My Room",                        "My_Room"),
        new("6_BRoom",                         "B Room",                         "BRoom"),
        new("7_DifferentEntrance",             "Different Entrance"),
        new("8_Pool",                          "Pool",                           "Pool"),
        new("9_Hallway",                       "Hallway",                        "Hallway_home"),
        new("10_RecordingRoom",                "Recording Room"),
        new("11_park",                         "Park"),
        new("12_Bakery",                       "Bakery",                         "Bakery"),
        new("13_Entrance",                     "Entrance",                       "Entrance"),
        new("14_Beach",                        "Beach",                          "Beach"),
        new("15_BeachPrivate",                 "Beach (Private)"),
        new("16_Forest",                       "Forest"),
        new("17_CarPark",                      "Car Park"),
        new("18_HallwayAtNight",               "Hallway (Night)"),
        new("19_Bar",                          "Bar",                            "Bar"),
        new("20_Street",                       "Street"),
        new("21_Suburban Exterior House",      "Suburban Exterior House",        "KenhouseOutside"),
        new("22_InsideCar",                    "Inside Car",                     "InsideCaar"),
        new("23_Church",                       "Church"),
        new("24_Suburban Living Room",         "Suburban Living Room",           "Kenhouseinside"),
        new("25_Mall",                         "Mall",                           "Mall"),
        new("26_Downtown",                     "Downtown",                       "Downtown"),
        new("27_Office",                       "Office",                         "Office"),
        new("28_Upstairs",                     "Upstairs",                       "Upstairs"),
        new("29_Wardrobe",                     "Wardrobe"),
        new("30_Clothstore",                   "Clothing Store",                 "Clothstore"),
        new("31_GeneralStore",                 "General Store",                  "generalstore"),
        new("32_StorageRoom",                  "Storage Room"),
        new("33_Club",                         "Club",                           "Club"),
        new("34_PublicToilet",                 "Public Toilet"),
        new("35_MansionOutside",               "Mansion (Outside)",              "Mansion"),
        new("36_MansionInside",                "Mansion (Inside)"),
        new("37_X",                            "X (37)"),
        new("38_X",                            "X (38)"),
        new("39_HotelLobby",                   "Hotel Lobby",                    "Hotel24Reception"),
        new("40_Hotelroom",                    "Hotel Room",                     "Hotelroom24"),
        new("41_Toystore",                     "Toy Store",                      "Sexstore"),
        new("42_Gym",                          "Gym",                            "Gym"),
        new("43_Basement",                     "Basement",                       "Basement"),
        new("44_Cardealership",                "Car Dealership",                 "CarDealership"),
        new("45_Hardwarestore",                "Hardware Store",                 "Techstore"),
        new("46_MallRestroom",                 "Mall Restroom",                  "MallToilet"),
        new("47_EndingZero",                   "Ending: Zero"),
        new("48_DrivingawayEnding",            "Ending: Driving Away"),
        new("49_SkyscraperEnding",             "Ending: Skyscraper"),
        new("50_Hikepath",                     "Hike Path"),
        new("51_HikeCamp",                     "Hike Camp"),
        new("52_Hotellobby",                   "Hotel Lobby (alt)"),
        new("53_Hotelroom",                    "Hotel Room (alt)",               "Hotelroom_Convention"),
        new("54_Convention",                   "Convention",                     "ConventionFans"),
        new("55_Stage",                        "Stage",                          "Stageroom"),
        new("56_Katehome",                     "Kate's Home",                    "Katehome"),
        new("57_Publicpool",                   "Public Pool",                    "publicpool"),
        new("58_Subpark",                      "Subpark",                        "publicparksuburbs"),
        new("59_Gasstation",                   "Gas Station",                    "Gasstation"),
        new("60_EndingAscend",                 "Ending: Ascend"),
        new("61_Endingyacht",                  "Ending: Yacht",                  "Yacht"),
        new("62_FantasyForest",                "Fantasy Forest"),
        new("63_FantasyForestCloudy",          "Fantasy Forest (Cloudy)"),
        new("64_FantasyCave",                  "Fantasy Cave"),
        new("65_FantasyForestDark",            "Fantasy Forest (Dark)"),
        new("66_ChloeRoom",                    "Chloe's Room",                   "ChloeStart"),
        new("67_Jap_ForestEntrance",           "Forest Entrance (JP)",           "EvergreenForest_Entrance"),
        new("68_Jap_Temple",                   "Temple (JP)",                    "Temple_Entrance"),
        new("69_Temple_Inside",                "Temple (Inside)",                "Temple_Inside"),
        new("70_Villa_Outside",                "Villa (Outside)",                "OutsideVilla"),
        new("71_Villa_Inside",                 "Villa (Inside)",                 "Villa_Lounge"),
        new("72_Villa_Backside",               "Villa (Backside)",               "Villa_Backyard"),
        new("73_VIlla_Sauna",                  "Villa Sauna",                    "Villa_Sauna"),
        new("74_VIlla_Library",                "Villa Library",                  "Villa_Library"),
        new("75_Villa_Office",                 "Villa Office",                   "Villa_Office"),
        new("76_Villa_Lab",                    "Villa Lab",                      "Villa_Lab"),
        new("77_Crossroad",                    "Crossroad"),
        new("78_SamanthaRoom",                 "Samantha's Room",                "SamanthaRoom"),
        new("79_TashaStore",                   "Tasha's Store",                  "Tasha_Store"),
        new("80_Shrine",                       "Shrine",                         "Shrine"),
        new("81_Abandonedshack",               "Abandoned Shack",                "ShackOutside"),
        new("82_Abandonedshackinside",         "Abandoned Shack (Inside)",       "ShackInside"),
        new("83_SofiaHome",                    "Sofia's Home",                   "SofiaHome"),
        new("84_HospitalEntrance",             "Hospital Entrance"),
        new("85_HospitalHallway",              "Hospital Hallway",               "Hospitalhallway"),
        new("86_DoctorsRoom",                  "Doctor's Room",                  "doctorroom"),
        new("87_Patientroom",                  "Patient Room",                   "PatientRoom"),
        new("88_VillaGuestRoom",               "Villa Guest Room",               "Villa_Guestroom"),
        new("89_ParkToilets",                  "Park Toilets"),
        new("90_JapaneseOldStreet",            "Japanese Old Street"),
        new("91_JapaneseStoreFront",           "Japanese Store Front"),
        new("92_JapanesePlaza",                "Japanese Plaza"),
        new("93_MaidCafe",                     "Maid Cafe"),
        new("94_JapaneseHotelRoom",            "Japanese Hotel Room"),
        new("95_JapaneseSubwaytrain",          "Japanese Subway Train"),
        new("96_JapaneseShrine",               "Japanese Shrine"),
        new("97_japaneseMassageParlor",        "Japanese Massage Parlor"),
        new("98_TropicalHotelRoom",            "Tropical Hotel Room"),
        new("99_TropicalBeach",                "Tropical Beach"),
        new("100_TropicalPool",                "Tropical Pool"),
        new("101_TropicalClub",                "Tropical Club"),
        new("102_WinterHotelRoom",             "Winter Hotel Room"),
        new("103_WinterVillageOutside",        "Winter Village (Outside)"),
        new("104_WinterHotspring",             "Winter Hotspring"),
        new("105_WinterMountains",             "Winter Mountains"),
        new("106_BeerTent",                    "Beer Tent"),
        new("107_JapanMaidCafeStorage",        "Japan Maid Cafe Storage"),
        new("108_WinterMountainsNight",        "Winter Mountains (Night)"),
        new("109_TropicalHotelRoomNight",      "Tropical Hotel Room (Night)"),
        new("110_BadlandsParkingLot",          "Badlands Parking Lot",            "Parkinglot_events"),
        new("111_BadlandsParkingLotBackside",  "Badlands Parking Lot (Backside)", "ParkingLotBackyard_Events"),
        new("112_GasStationInterior",          "Gas Station (Interior)",          "Gasstation_Inside"),
        new("113_ToniHome",                    "Toni's Home",                     "ToniHome_Events"),
        new("114_HimariDiningRoom",            "Himari Dining Room"),
        new("115_HimariBathRoom",              "Himari Bathroom"),
        new("116_CelesteHome",                 "Celeste's Home",                  "CelesteHome"),
        new("117_ElevatorInside",              "Elevator (Inside)"),
        new("118_AmusementParkAbandoned",      "Amusement Park (Abandoned)",      "AbandonedAmusementPark"),
        new("119_AirDuct",                     "Air Duct"),
        new("120_FlowerStore",                 "Flower Store",                    "FlowerStoreCore"),
        new("121_DiningRoomHome",              "Dining Room (Home)"),
        new("122_Cinema",                      "Cinema"),
        new("123_CharlotteHalloweenHome",      "Charlotte Halloween (Home)"),
        new("124_CharlotteHalloweenBathroom",  "Charlotte Halloween (Bathroom)"),
        new("125_BarnBackInterior",            "Barn (Back Interior)"),
        new("126_FarmPath",                    "Farm Path",                       "C_Farmmap_Road"),
        new("127_FarmOutside",                 "Farm (Outside)",                  "C_Farmmap_OutsideHouse"),
        new("128_FarmLivingRoom",              "Farm Living Room",                "C_Farmmap_InsideHouse"),
        new("129_SubParkRestroom",             "Subpark Restroom",                "C_SubParkRestRoom"),
        new("130_AppleTree",                   "Apple Tree",                      "C_Farmmap_AppleTree"),
        new("131_SecretMinoShelter",           "Secret Mino Shelter",             "C_Farmmap_SecretMinoShelter"),
        new("132_MotelRoom69",                 "Motel Room 69",                   "C_Motelroom69"),
        new("133_HarborDistrict",              "Harbor District",                 "C_HarborDistrict"),
        new("134_HarborWarehouse",             "Harbor Warehouse"),
        new("135_HarborScifiLab",              "Harbor Sci-fi Lab",               "C_EvelynSecretLab"),
        new("136_HarborScifiPrison",           "Harbor Sci-fi Prison",            "C_EvelynPlasmaCell"),
        new("137_HarborHauntedHouse",          "Harbor Haunted House",            "C_HauntedHouse"),
        new("138_HikingPath_Start",            "Hiking Path (Start)"),
        new("139_HikingPath_WildForest",       "Hiking Path (Wild Forest)",       "C_HikingPath_DeepForest"),
        new("140_HikingPath_Lake",             "Hiking Path (Lake)"),
        new("141_HikingPathShrine",            "Hiking Path Shrine"),
        new("142_NightDistrict",               "Night District",                  "C_NeoNRow"),
        new("143_PublicLibrary",               "Public Library",                  "C_Library"),
        new("144_HarborSewers",                "Harbor Sewers",                   "C_HarborSewers"),
        new("145_CasinoMain",                  "Casino (Main)",                   "C_Casino"),
    };

    /// <summary>Lookup by GO name. Returns null if the name isn't a known vanilla place.</summary>
    public static VanillaPlace? FindByGoName(string goName)
    {
        foreach (var p in All)
            if (p.GoName == goName) return p;
        return null;
    }

    /// <summary>
    /// The roomtalk token for a level token, or "" when there is none.
    /// <para/>
    /// Only vanilla levels have one, and not even all of them — intro/ending
    /// levels and sub-locations of larger areas have no roomtalk, so a dialogue
    /// there simply has no vanilla entry dialogue to take priority over. A
    /// pack-authored level (<c>place:</c>) has no vanilla roomtalk by
    /// definition: the pack's own is shared housekeeping with its Conditions
    /// stripped, so there is nothing there to suppress either.
    /// </summary>
    public static string RoomTalkTokenForLevel(string? levelToken)
    {
        if (string.IsNullOrWhiteSpace(levelToken)) return "";
        const string prefix = "vanilla:";
        if (!levelToken!.StartsWith(prefix, System.StringComparison.Ordinal)) return "";
        var place = FindByGoName(levelToken.Substring(prefix.Length));
        return string.IsNullOrEmpty(place?.RoomTalkName) ? "" : "vanilla:" + place!.RoomTalkName;
    }

    /// <summary>True when a level has a vanilla roomtalk, i.e. when
    /// "Prioritize this dialogue over vanilla" can do anything there.</summary>
    public static bool HasRoomTalk(string? levelToken) =>
        !string.IsNullOrEmpty(RoomTalkTokenForLevel(levelToken));
}
