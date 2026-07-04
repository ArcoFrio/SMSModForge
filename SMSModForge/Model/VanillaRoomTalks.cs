using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Catalog of vanilla <c>8_Room_Talk</c> children in Starmaker Story 1.8E.
/// Used by the dialogue editor's roomtalk target picker so packs can hang a
/// dialogue off an existing in-game location's dialogue list. Each roomtalk
/// GameObject in the scene is a parent for one or more child <c>Dialogue</c>
/// MonoBehaviours; a pack-authored dialogue is instantiated as another child
/// under the same parent.
/// <para/>
/// Some roomtalks are state-variants of others (e.g. <c>Bakery_Aftermath</c>
/// is the bakery's "after the cake event" version). Some don't pair to a
/// <see cref="VanillaPlaces"/> entry at all (sub-events, retriggers).
/// Derived from <c>a decompiled scene-hierarchy dump of the target game</c>
/// lines 14274 – 16341.
/// </summary>
public static class VanillaRoomTalks
{
    public sealed record VanillaRoomTalk(string Name, string DisplayName);

    /// <summary>Every vanilla child of <c>8_Room_Talk</c>, scene-sibling order.</summary>
    public static readonly IReadOnlyList<VanillaRoomTalk> All = new VanillaRoomTalk[]
    {
        new("Bakery",                            "Bakery"),
        new("Bakery_Aftermath",                  "Bakery (Aftermath)"),
        new("Gym",                               "Gym"),
        new("Gym_Aftermath",                     "Gym (Aftermath)"),
        new("Office",                            "Office"),
        new("masterbedroom",                     "Master Bedroom"),
        new("My_Room",                           "My Room"),
        new("Garage",                            "Garage"),
        new("MallToilet",                        "Mall Restroom"),
        new("Downtown",                          "Downtown"),
        new("Mall",                              "Mall"),
        new("InsideCaar",                        "Inside Car"),
        new("Clothstore",                        "Clothing Store"),
        new("generalstore",                      "General Store"),
        new("Mansion",                           "Mansion (Outside)"),
        new("BRoom",                             "B Room"),
        new("Kitchen",                           "Kitchen"),
        new("Livingroom",                        "Living Room"),
        new("Club",                              "Club"),
        new("Pool",                              "Pool"),
        new("Pool_DifferentDay",                 "Pool (Different Day)"),
        new("ChloeStart",                        "Chloe Start"),
        new("ClubRestroom",                      "Club Restroom"),
        new("Bath",                              "Bath"),
        new("Basement",                          "Basement"),
        new("Basement_Retrigger",                "Basement (Retrigger)"),
        new("Entrance",                          "Entrance"),
        new("CarDealership",                     "Car Dealership"),
        new("Techstore",                         "Hardware Store"),
        new("KenhouseOutside",                   "Suburban Exterior House"),
        new("Afterhacking",                      "After Hacking"),
        new("Kenhouseinside",                    "Suburban Living Room"),
        new("Bar",                               "Bar"),
        new("Sexstore",                          "Toy Store"),
        new("Hotelroom24",                       "Hotel Room"),
        new("Beach",                             "Beach"),
        new("Hotelroom_Convention",              "Hotel Room (Convention)"),
        new("Stageroom",                         "Stage"),
        new("ConventionFans",                    "Convention"),
        new("Upstairs",                          "Upstairs"),
        new("Katehome",                          "Kate's Home"),
        new("Yacht",                             "Yacht"),
        new("Gasstation",                        "Gas Station"),
        new("Gasstation_Inside",                 "Gas Station (Interior)"),
        new("publicparksuburbs",                 "Subpark"),
        new("publicpool",                        "Public Pool"),
        new("OutsideVilla",                      "Villa (Outside)"),
        new("Villa_Lounge",                      "Villa (Inside)"),
        new("Villa_Office",                      "Villa Office"),
        new("Comapreafterwtalk",                 "Compare After W Talk"),
        new("Villa_Sauna",                       "Villa Sauna"),
        new("Villa_Backyard",                    "Villa (Backside)"),
        new("Villa_Library",                     "Villa Library"),
        new("Villa_Lab",                         "Villa Lab"),
        new("SamanthaRoom",                      "Samantha's Room"),
        new("Tasha_Store",                       "Tasha's Store"),
        new("Tasha_Store_AfterTabletop",         "Tasha's Store (After Tabletop)"),
        new("Temple_Inside",                     "Temple (Inside)"),
        new("EvergreenForest_Entrance",          "Forest Entrance (JP)"),
        new("Shrine",                            "Shrine"),
        new("Temple_Entrance",                   "Temple (JP)"),
        new("PatientRoom",                       "Patient Room"),
        new("SofiaHome",                         "Sofia's Home"),
        new("ShackOutside",                      "Abandoned Shack"),
        new("ShackInside",                       "Abandoned Shack (Inside)"),
        new("Villa_Guestroom",                   "Villa Guest Room"),
        new("doctorroom",                        "Doctor's Room"),
        new("Hospitalhallway",                   "Hospital Hallway"),
        new("Parkinglot_events",                 "Badlands Parking Lot"),
        new("ParkingLotBackyard_Events",         "Badlands Parking Lot (Backside)"),
        new("ToniHome_Events",                   "Toni's Home"),
        new("AbandonedAmusementPark",            "Amusement Park (Abandoned)"),
        new("CelesteHome",                       "Celeste's Home"),
        new("Hallway_home",                      "Hallway"),
        new("Hotel24Reception",                  "Hotel Lobby"),
        new("FlowerStoreCore",                   "Flower Store"),
        new("DemonSummoninEvent",                "Demon Summoning Event"),
        new("C_Farmmap_Road",                    "Farm Path"),
        new("C_Farmmap_AppleTree",               "Apple Tree"),
        new("C_Farmmap_InsideHouse",             "Farm Living Room"),
        new("C_Farmmap_OutsideHouse",            "Farm (Outside)"),
        new("C_Farmmap_SecretMinoShelter",       "Secret Mino Shelter"),
        new("C_SubParkRestRoom",                 "Subpark Restroom"),
        new("C_Motelroom69",                     "Motel Room 69"),
        new("C_HarborDistrict",                  "Harbor District"),
        new("C_EvelynSecretLab",                 "Harbor Sci-fi Lab"),
        new("C_EvelynPlasmaCell",                "Harbor Sci-fi Prison"),
        new("C_NeoNRow",                         "Night District"),
        new("C_HikingPathEnd",                   "Hiking Path (End)"),
        new("C_HarborSewers",                    "Harbor Sewers"),
        new("C_HikingPath_DeepForest",           "Hiking Path (Wild Forest)"),
        new("C_Library",                         "Public Library"),
        new("C_HauntedHouse",                    "Harbor Haunted House"),
        new("C_Casino",                          "Casino (Main)"),
    };

    /// <summary>Lookup by GO name. Returns null if the name isn't a known vanilla roomtalk.</summary>
    public static VanillaRoomTalk? FindByName(string name)
    {
        foreach (var r in All)
            if (r.Name == name) return r;
        return null;
    }
}
