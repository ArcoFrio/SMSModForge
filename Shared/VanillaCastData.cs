namespace SMSModForge.Shared
{
    /// <summary>
    /// The game's busts, and who the game says wears them.
    /// <para/>
    /// Compiled into both projects from one file, for the same reason the
    /// substitution parser is: the editor offers these characters to an author
    /// and the runtime has to resolve the ones an author used, and two copies
    /// of the list drift the moment one is corrected.
    /// <para/>
    /// It living here is also what lets a pack NOT write them down. A manifest
    /// used to carry a whole vanilla character - name, and every one of Anna's
    /// sixty-four outfits - as soon as a single line was spoken by her, purely
    /// so the runtime would know what to activate. The runtime knows now, so
    /// the manifest carries a vanilla character only when the author has
    /// actually changed something about them.
    /// <para/>
    /// GENERATED from the editor's reviewed catalog rather than typed. See
    /// SMSModForge/Model/VanillaBusts.cs for what is deliberately NOT in it.
    /// </summary>
    public static class VanillaCastData
    {
        /// <summary>One bust GameObject, and the character it is filed under.</summary>
        public sealed class VanillaBust
        {
            public VanillaBust(string goName, string character)
            {
                GoName = goName;
                Character = character;
            }

            public string GoName { get; private set; }
            public string Character { get; private set; }
        }

        /// <summary>Every direct child of <c>2_Bust_Manager</c> the tool
        /// offers, in the order the scene has them.</summary>
        public static readonly VanillaBust[] Busts =
        {
            new VanillaBust("Anna_Bust", "Anna"),
            new VanillaBust("Anna_Gym", "Anna"),
            new VanillaBust("Anna_GreyHoodie", "Anna"),
            new VanillaBust("AnnaBedroom", "Anna"),
            new VanillaBust("AnnaWedding", "Anna"),
            new VanillaBust("Anna_Wine", "Anna"),
            new VanillaBust("Anna_YellowSexy", "Anna"),
            new VanillaBust("Anna_Towel", "Anna"),
            new VanillaBust("Anna_Nude", "Anna"),
            new VanillaBust("Anna_BathrobeClosed", "Anna"),
            new VanillaBust("Anna_White_Lingerie", "Anna"),
            new VanillaBust("Anna_Black_Lingerie", "Anna"),
            new VanillaBust("Anna_Swimwear", "Anna"),
            new VanillaBust("Anna_Red_Pullover", "Anna"),
            new VanillaBust("Anna_GoldenBikini", "Anna"),
            new VanillaBust("Anna_CoolOutfit", "Anna"),
            new VanillaBust("Anna_RedTanktopo", "Anna"),
            new VanillaBust("Anna_RedOfficesuit", "Anna"),
            new VanillaBust("Anna_Maid", "Anna"),
            new VanillaBust("Anna_Bimb", "Anna"),
            new VanillaBust("Anna_Elf", "Anna"),
            new VanillaBust("Anna_Hiking", "Anna"),
            new VanillaBust("Anna_LoveRoute", "Anna"),
            new VanillaBust("Anna_SlutRoute", "Anna"),
            new VanillaBust("Anna_Gaming", "Anna"),
            new VanillaBust("Anna_Teacher", "Anna"),
            new VanillaBust("Anna_Bunny", "Anna"),
            new VanillaBust("Anna_SexWorker", "Anna"),
            new VanillaBust("Anna_Garden", "Anna"),
            new VanillaBust("Anna_China", "Anna"),
            new VanillaBust("Anna_Trenchcoat", "Anna"),
            new VanillaBust("Anna_NudeCovering", "Anna"),
            new VanillaBust("Anna_Christmaswrapping", "Anna"),
            new VanillaBust("Anna_GreenPullover", "Anna"),
            new VanillaBust("Anna_WinterSlutty", "Anna"),
            new VanillaBust("Anna_TropicalDress", "Anna"),
            new VanillaBust("Anna_WhiteSwimsuitTropical", "Anna"),
            new VanillaBust("Anna_WinterOutfit", "Anna"),
            new VanillaBust("Anna_GoddessBikini", "Anna"),
            new VanillaBust("Anna_SilverDressDeluxe", "Anna"),
            new VanillaBust("Anna_YogaOutfit", "Anna"),
            new VanillaBust("Anna_ChainmailBikini", "Anna"),
            new VanillaBust("Anna_WitchDress", "Anna"),
            new VanillaBust("Anna_SuperheroOutfit", "Anna"),
            new VanillaBust("AnnaSunDressBust", "Anna"),
            new VanillaBust("AnnaDefaultIceCreamBust", "Anna"),
            new VanillaBust("Anna_ChristmasEventBust", "Anna"),
            new VanillaBust("Anna_ChristmasLingerieBust", "Anna"),
            new VanillaBust("Anna_PlayerBirthdayLingerie_Bust", "Anna"),
            new VanillaBust("Anna_Newyearslingerie_Bust", "Anna"),
            new VanillaBust("Anna_NewyearsDress_Bust", "Anna"),
            new VanillaBust("Anna_CowBust", "Anna"),
            new VanillaBust("Anna_DemonBust", "Anna"),
            new VanillaBust("Anna_StudioElfBust", "Anna"),
            new VanillaBust("Anna_StudioCowBust", "Anna"),
            new VanillaBust("Anna_BlackBoxersBust", "Anna"),
            new VanillaBust("Anna_MegaBoobsBust", "Anna"),
            new VanillaBust("Anna_FutaBust", "Anna"),
            new VanillaBust("Anna_SlutRouteFishnetBust", "Anna"),
            new VanillaBust("Anna_LoveRouteRedSwimsuitBust", "Anna"),
            new VanillaBust("AnnaBust_TightBust_Purpledress", "Anna"),
            new VanillaBust("AnnaBust_TightBust_Gym", "Anna"),
            new VanillaBust("AnnaBust_TightBust_CyanTop", "Anna"),
            new VanillaBust("AnnaBust_OilUp_BlackBikini", "Anna"),
            new VanillaBust("Nikki_Bust_Default", "Nikki"),
            new VanillaBust("Nikki_Bust_Topless", "Nikki"),
            new VanillaBust("Android_normaloutfit", "Android"),
            new VanillaBust("CharlotteBust", "Charlotte"),
            new VanillaBust("Charlotte_Nude", "Charlotte"),
            new VanillaBust("Charlotte_Dress", "Charlotte"),
            new VanillaBust("Charlotte_BNikini", "Charlotte"),
            new VanillaBust("Charlotte_Agentbust", "Charlotte"),
            new VanillaBust("Charlotte_ChristmasDressBust", "Charlotte"),
            new VanillaBust("Charlotte_BirthdayOutfit", "Charlotte"),
            new VanillaBust("Charlotte_PirateOutfit_Bust", "Charlotte"),
            new VanillaBust("Charlotte_RedBikini", "Charlotte"),
            new VanillaBust("ClaudiaBust_Default", "Claudia"),
            new VanillaBust("ClaudiaBust_StarCon", "Claudia"),
            new VanillaBust("Claudia_StreetinterviewBust", "Claudia"),
            new VanillaBust("Choe_Base", "Choe"),
            new VanillaBust("Choe_Nude", "Choe"),
            new VanillaBust("Tasha_Default", "Tasha"),
            new VanillaBust("NyxaraBust_Default", "Nyxara"),
            new VanillaBust("NyxaraBust_WorkerBust", "Nyxara"),
            new VanillaBust("NyxaraBust_Dark", "Nyxara"),
            new VanillaBust("Josef", "Josef"),
            new VanillaBust("Josef_sprot", "Josef"),
            new VanillaBust("JosefSmoking", "Josef"),
            new VanillaBust("JosefDout", "Josef"),
            new VanillaBust("Josef_ChristmasBust", "Josef"),
            new VanillaBust("Josef_HalloweenBust", "Josef"),
            new VanillaBust("Josef_CasualShirtBust", "Josef"),
            new VanillaBust("Adrian_bust", "Adrian"),
            new VanillaBust("Adrian_Suit", "Adrian"),
            new VanillaBust("Adrian_Sport", "Adrian"),
            new VanillaBust("Adrian_SemiNude", "Adrian"),
            new VanillaBust("Adrian_HalloweenBust", "Adrian"),
            new VanillaBust("Isabella", "Isabella"),
            new VanillaBust("Isabella_Swimsuit", "Isabella"),
            new VanillaBust("Isabella_Lingerie", "Isabella"),
            new VanillaBust("Sofia_Police", "Sofia"),
            new VanillaBust("Sofia_Undercover", "Sofia"),
            new VanillaBust("Sofia_Nude", "Sofia"),
            new VanillaBust("Sofia_Dress", "Sofia"),
            new VanillaBust("Sofia_Slut", "Sofia"),
            new VanillaBust("Sofia_Cow", "Sofia"),
            new VanillaBust("Sofia_Lingerie", "Sofia"),
            new VanillaBust("Katarina_Normal", "Katarina"),
            new VanillaBust("Katarina_Nude", "Katarina"),
            new VanillaBust("Katarina_Swimwear", "Katarina"),
            new VanillaBust("Katarina_Cowbust", "Katarina"),
            new VanillaBust("Emma_Normal", "Emma"),
            new VanillaBust("Emma_Swimwear", "Emma"),
            new VanillaBust("Emma_Nude", "Emma"),
            new VanillaBust("Emma_Lingerie", "Emma"),
            new VanillaBust("Emma_Cowbust", "Emma"),
            new VanillaBust("NurseNina_Default", "NurseNina"),
            new VanillaBust("Emma_Towel", "Emma"),
            new VanillaBust("Mario_Normal", "Mario"),
            new VanillaBust("Mario_Nude", "Mario"),
            new VanillaBust("Master_Default", "Master"),
            new VanillaBust("Nadia_RedSwimsuitBust", "Nadia"),
            new VanillaBust("Haniya", "Haniya"),
            new VanillaBust("Haniya_dress", "Haniya"),
            new VanillaBust("Haniya_bunnysuit", "Haniya"),
            new VanillaBust("Haniya_nude", "Haniya"),
            new VanillaBust("Haniya_CasualBust", "Haniya"),
            new VanillaBust("XXX_Celeste_Bust", "Celeste"),
            new VanillaBust("Rockerguy_Bust", "Rockerguy"),
            new VanillaBust("TomboyToni_BustDefault", "Toni"),
            new VanillaBust("TomboyToni_BustCutie", "Toni"),
            new VanillaBust("TomboyToni_BustBaroutfit", "Toni"),
            new VanillaBust("TomboyToni_Newyearsdressbust", "Toni"),
            new VanillaBust("TomboyToni_BustDefaultCLEAN", "Toni"),
            new VanillaBust("River_Base", "River"),
            new VanillaBust("River_Dress", "River"),
            new VanillaBust("River_Lingerie", "River"),
            new VanillaBust("Kate", "Kate"),
            new VanillaBust("KateUndies", "Kate"),
            new VanillaBust("KateDress", "Kate"),
            new VanillaBust("KatePullover", "Kate"),
            new VanillaBust("KateBikini", "Kate"),
            new VanillaBust("Kateninjabust", "Kate"),
            new VanillaBust("KateCasualShirtbust", "Kate"),
            new VanillaBust("Amelia_defaultbust", "Amelia"),
            new VanillaBust("Amelia_Barista", "Amelia"),
            new VanillaBust("Amelia_Beach", "Amelia"),
            new VanillaBust("Amelia_Carwash", "Amelia"),
            new VanillaBust("Amelia_NewYearsEveBust", "Amelia"),
            new VanillaBust("Amelia_Cowbust", "Amelia"),
            new VanillaBust("Amelia_BaristaMilkyBust", "Amelia"),
            new VanillaBust("Amelia_BrownJacketBust", "Amelia"),
            new VanillaBust("Ken", "Ken"),
            new VanillaBust("Ken_D_Out", "Ken"),
            new VanillaBust("Ken_Swimsuit", "Ken"),
            new VanillaBust("Zuri", "Zuri"),
            new VanillaBust("Zuri_CasualBarBust", "Zuri"),
            new VanillaBust("Zuri_MotelBust", "Zuri"),
            new VanillaBust("AliceBust", "Alice"),
            new VanillaBust("AliceDress", "Alice"),
            new VanillaBust("AliceSexy", "Alice"),
            new VanillaBust("AliceSwimwear", "Alice"),
            new VanillaBust("Alice_GhostBust", "Alice"),
            new VanillaBust("Alice_GhostFaceOutBust", "Alice"),
            new VanillaBust("NorahDefault_Bust", "Norah"),
            new VanillaBust("NorahWhiteBikiniBust", "Norah"),
            new VanillaBust("SamuelMalePastor_Bust", "Samuel"),
            new VanillaBust("SamuelSwimBust", "Samuel"),
            new VanillaBust("Gabriel_Bust", "Gabriel"),
            new VanillaBust("SamanthaBust", "Samantha"),
            new VanillaBust("Samantha_Swimsuit", "Samantha"),
            new VanillaBust("Samantha_SwimsuitSlutty", "Samantha"),
            new VanillaBust("Samantha_Nude", "Samantha"),
            new VanillaBust("Samantha_Gaming", "Samantha"),
            new VanillaBust("SamanthaBust_ComfyCasualOutside", "Samantha"),
            new VanillaBust("PhoenixBust", "Phoenix"),
            new VanillaBust("Phoenixbust_Casual", "Phoenix"),
            new VanillaBust("PhoenixBust_Suit", "Phoenix"),
            new VanillaBust("PhoenixBust_Swimsuit", "Phoenix"),
            new VanillaBust("JoeyBust", "Joey"),
            new VanillaBust("JoeyCoolCasualBust", "Joey"),
            new VanillaBust("HimariBust", "Himari"),
            new VanillaBust("Himari_Beachbust", "Himari"),
            new VanillaBust("Himari_SexySweaterBust", "Himari"),
            new VanillaBust("HimariMotherBust", "Himari (Mother)"),
            new VanillaBust("HimariMotherBustRoseLingerie", "Himari (Mother)"),
            new VanillaBust("HimariFatherBust", "Himari (Father)"),
            new VanillaBust("KirbyBustDefault", "Kirby"),
            new VanillaBust("KirbyBustNude", "Kirby"),
            new VanillaBust("KirbyBustNCowgirl", "Kirby"),
            new VanillaBust("KirbyBustCasualOutfit", "Kirby"),
            new VanillaBust("S_ClayDefaultBust", "Clay"),
            new VanillaBust("Robert_Bust", "Robert"),
            new VanillaBust("AstridStoreBust", "Astrid"),
            new VanillaBust("AstridPinkBikiniBust", "Astrid"),
            new VanillaBust("Astrid_CasualRed_Bust", "Astrid"),
            new VanillaBust("LizBust", "Liz"),
            new VanillaBust("doctorfrost_default", "Doctor Frost"),
            new VanillaBust("doctorfrost_default_PReg", "Doctor Frost"),
            new VanillaBust("doctorfrost_Casualbust", "Doctor Frost"),
            new VanillaBust("doctorfrost_Casualbust_Preg", "Doctor Frost"),
            new VanillaBust("doctorfrost_bdsm", "Doctor Frost"),
            new VanillaBust("doctorfrost_privatelabbust", "Doctor Frost"),
            new VanillaBust("doctorfrost_privatelab_Pregbust", "Doctor Frost"),
            new VanillaBust("Vanessa_Bust_Nun", "Vanessa"),
            new VanillaBust("Vanessa_Bust_LibraryOutfit", "Vanessa"),
            new VanillaBust("S_Bouncer", "Bouncer"),
            new VanillaBust("S_Elbandito", "El Bandito"),
            new VanillaBust("S_ShadyVendor", "Shady Vendor"),
            new VanillaBust("S_ShadyVendor_BathrobeBust", "Shady Vendor"),
            new VanillaBust("S_HarborCopBust", "Harbor Cop"),
            new VanillaBust("Richwomanblonde", "Rich Woman (Blonde)"),
            new VanillaBust("Richwomanpink", "Rich Woman (Pink)"),
            new VanillaBust("Richguy", "Rich Guy"),
            new VanillaBust("Jasper_Default", "Jasper"),
            new VanillaBust("Roxy_Default", "Roxy"),
            new VanillaBust("V_ChihiroDefault", "Chihiro (Vacation)"),
            new VanillaBust("V_FreyaDefault", "Freya (Vacation)"),
            new VanillaBust("V_LiamDefault", "Liam (Vacation)"),
            new VanillaBust("V_LeilaniDefault", "Leilani (Vacation)"),
            new VanillaBust("V_LeilaniBikiniBust", "Leilani (Vacation)"),
            new VanillaBust("V_MeiDefault", "Mei (Vacation)"),
            new VanillaBust("V_MeiRedDress", "Mei (Vacation)"),
            new VanillaBust("V_MeiNude", "Mei (Vacation)"),
            new VanillaBust("V_MichelleDefault", "Michelle (Vacation)"),
            new VanillaBust("V_RikuDefault", "Riku (Vacation)"),
            new VanillaBust("V_SakuraDefault", "Sakura (Vacation)"),
            new VanillaBust("V_S_JudgeBenjamin", "Judge Benjamin (Vacation)"),
            new VanillaBust("V_S_Toshiro", "Toshiro (Vacation)"),
            new VanillaBust("V_S_Daiju", "Daiju (Vacation)"),
            new VanillaBust("V_S_Hiroji", "Hiroji (Vacation)"),
            new VanillaBust("V_S_Sora", "Sora (Vacation)"),
            new VanillaBust("V_S_Diego", "Diego (Vacation)"),
            new VanillaBust("V_S_Clara", "Clara (Vacation)"),
            new VanillaBust("V_S_Felix", "Felix (Vacation)"),
            new VanillaBust("V_S_CrazyOldMan", "Crazy Old Man (Vacation)"),
            new VanillaBust("V_S_Wolfguy", "Wolfguy (Vacation)"),
            new VanillaBust("V_S_Wolfgirl", "Wolfgirl (Vacation)"),
            new VanillaBust("S_Carina", "Carina"),
            new VanillaBust("S_Elfina", "Elfina"),
            new VanillaBust("S_FanBlueShirt", "Fan: Blue Shirt"),
            new VanillaBust("S_FanAsian", "Fan: Asian"),
            new VanillaBust("S_FanGirl", "Fan: Girl"),
            new VanillaBust("S_FanOldMan", "Fan: Old Man"),
            new VanillaBust("S_FanFemboy", "Fan: Femboy"),
            new VanillaBust("S_FanMuscle", "Fan: Muscle"),
            new VanillaBust("S_FanJeff", "Fan: Jeff"),
            new VanillaBust("S_ParkWoman1", "Park Woman"),
            new VanillaBust("S_ParkMan", "Park Man"),
            new VanillaBust("S_Mobster1", "Mobster 1"),
            new VanillaBust("S_Mobster2", "Mobster 2"),
            new VanillaBust("S_Technician", "Technician"),
            new VanillaBust("S_Blackguy", "Black Guy"),
            new VanillaBust("S_Ghost", "Ghost"),
            new VanillaBust("S_CelesteAgent", "Celeste Agent"),
            new VanillaBust("S_Trenchcoatmilfbust", "Trenchcoat MILF"),
            new VanillaBust("S_LadyNoireBust", "Lady Noire"),
            new VanillaBust("S_Flowercustomer", "Flower Customer"),
            new VanillaBust("S_Succubus", "Succubus"),
            new VanillaBust("S_RedDemon", "Red Demon"),
            new VanillaBust("S_AnnaMummyOutfit", "Anna (Mummy Outfit)"),
            new VanillaBust("S_Nightheart", "Nightheart"),
            new VanillaBust("S_PopPop", "PopPop"),
            new VanillaBust("S_RichCEOBust", "Rich CEO"),
            new VanillaBust("S_Minitalk_NPC_DowntownBlonde", "Downtown Blonde"),
            new VanillaBust("S_Minitalk_NPC_BigButtCityWoman", "Big-Butt City Woman"),
            new VanillaBust("S_Minitalk_NPC_CityGingerGlasse", "City Ginger Glasses"),
            new VanillaBust("S_Minitalk_NPC_RedShirtCityGuy", "Red-Shirt City Guy"),
            new VanillaBust("S_Minitalk_NPC_ShyCityGuy", "Shy City Guy"),
            new VanillaBust("S_Minitalk_NPC_OldCityGentleman", "Old City Gentleman"),
            new VanillaBust("S_Minitalk_NPC_ChubbyBackpackGuy", "Chubby Backpack Guy"),
            new VanillaBust("S_Minitalk_NPC_RedhairMaleModel", "Redhair Male Model"),
            new VanillaBust("S_Minitalk_NPC_NeonRowPinkWorker", "Neon Row Pink Worker"),
            new VanillaBust("S_Minitalk_NPC_BlondeNeonRowWorker", "Blonde Neon Row Worker"),
            new VanillaBust("S_Minitalk_NPC_UsedBoobaWorker", "Used-Booba Worker"),
            new VanillaBust("S_Minitalk_NPC_AverageWoman", "Average Woman"),
            new VanillaBust("S_Minitalk_NPC_RichCustomerFemale", "Rich Customer (Female)"),
            new VanillaBust("S_Minitalk_NPC_KateFriend", "Kate's Friend"),
            new VanillaBust("S_BigFootBust", "BigFoot"),
            new VanillaBust("S_LizardGuyBust", "Lizard Guy"),
            new VanillaBust("S_Evelyn_AlienBust", "Evelyn (Alien)"),
            new VanillaBust("S_Poolchick_Blonde", "Pool Chick (Blonde)"),
            new VanillaBust("S_Poolchick_Reddie", "Pool Chick (Reddie)"),
            new VanillaBust("S_Bust_Mini_Mallwaifu_BlueHair", "Mall Waifu (Blue Hair)"),
            new VanillaBust("S_Bust_Mini_Mallwaifu_Blonde", "Mall Waifu (Blonde)"),
        };

        /// <summary>
        /// What the game CALLS whoever a group of busts describes, where the
        /// bust files disagree - a misspelling, a description standing in for
        /// a name, or a curator's note left in it.
        /// <para/>
        /// Every entry is reviewed by hand; see the editor's VanillaCharacters
        /// for the reasoning behind each, which is the part worth keeping.
        /// </summary>
        public static readonly System.Collections.Generic.Dictionary<string, string> SpokenNames =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            // Misspelt or misspaced in the bust files.
            { "Choe", "Chloe" },
            { "NurseNina", "Nurse Nina" },
            { "PopPop", "Pop Pop" },
            { "Bouncer", "The Bouncer" },

            // Described rather than named.
            { "El Bandito", "Frank" },   // S_Elbandito speaks as Frank
            { "Succubus", "Zazia" },   // S_Succubus speaks as Zazia
            { "Himari (Father)", "Mr. Kimura" },   // HimariDad speaks as Mr. Kimura
            { "Himari (Mother)", "Mrs. Kimura" },   // HimariMom speaks as Mrs. Kimura

            // An outfit filed as a person: folded back onto her.
            { "Anna (Mummy Outfit)", "Anna" },

            // Named out loud in the one scene it appears in, by the only person
            // who speaks there: "This is not an 'it.' It's Subject IX-Delta. One
            // of my finest creations." The bust file calls it Evelyn's alien,
            // which is wrong twice over - it is not Evelyn, and the same scene
            // says "Not from space. From me."
            { "Evelyn (Alien)", "Subject IX-Delta" },

            // Named by the conversations rather than by the bust files.
            //
            // Worked out by elimination: in a scene where every bust but one
            // belongs to somebody already identified, and every speaker but one is
            // likewise accounted for, the two left over are each other. Each of
            // these was forced that way by at least two independent scenes with
            // nothing disagreeing - the count below - and each names an actor that
            // carries a real name rather than a file name.
            //
            // That last test is what makes the list short - elimination on its own
            // also offered several readings from a single scene, where two people
            // sharing one conversation is all the evidence there is.
            //
            // It is not proof of nonsense, though. Elimination offered "Evelyn is
            // Doctor Frost", which was filed here as an error and is in fact
            // correct: she is Doctor Evelyn Frost. What was wrong was the bust it
            // was reasoning about - S_Evelyn_AlienBust is Subject IX-Delta, her
            // creation, which appears in her scene and never speaks - so the
            // answer was right about the people and wrong about the picture.
            //
            // That silence is why elimination reached for her name at all: the
            // scene has one speaker and two bodies. The dialogue names the second
            // one in a line, which is a better source than any inference.
            { "Master", "Master Zhen" },   // 14 scenes
            { "Shady Vendor", "Miss Zero" },   // 8 scenes
            { "Android", "Cerise" },   // 2 scenes
            { "Rockerguy", "Jack" },   // 2 scenes

            // Confirmed by the pack's author, who knows the game. A single scene
            // pairing two people is thin evidence on its own, but it is evidence,
            // and the name it yields is the game's own rather than a description
            // somebody wrote for a bust file.
            { "Rich Guy", "Tristan" },
            { "Kate's Friend", "Megan" },
            { "Harbor Cop", "Cop" },
            { "Celeste Agent", "Agent" },
            { "Downtown Blonde", "Civilian" },
            { "Average Woman", "Normal Woman" },
            { "Lizard Guy", "Creature" },
            { "Redhair Male Model", "Cool Guy" },
            { "Used-Booba Worker", "Sex Worker" },
            { "Rich Customer (Female)", "Rich Customer" },

            // Three more the same reasoning offered are NOT here, because the name
            // the game gives is ALREADY somebody else's: the alien in Doctor
            // Frost's scene would have become Doctor Frost, the judge would have
            // become Leilani, and both pool chicks would have become "Stranger" -
            // one name for two women who have no other way of being told apart.
            // A rename that merges two people is worse than a clumsy description.

            // Where somebody appears is not part of their name. Each base name
            // below is an actor the game has, which is what makes the tag safe to
            // drop. "Evelyn (Alien)" is not one of these: dropping ITS tag would
            // have produced the first name of a different character entirely -
            // Doctor Evelyn Frost - so it is renamed outright, above, to the name
            // the dialogue gives it.
            { "Chihiro (Vacation)", "Chihiro" },
            { "Clara (Vacation)", "Clara" },
            { "Crazy Old Man (Vacation)", "Crazy Old Man" },
            { "Daiju (Vacation)", "Daiju" },
            { "Diego (Vacation)", "Diego" },
            { "Felix (Vacation)", "Felix" },
            { "Freya (Vacation)", "Freya" },
            { "Hiroji (Vacation)", "Hiroji" },
            { "Judge Benjamin (Vacation)", "Judge Benjamin" },
            { "Leilani (Vacation)", "Leilani" },
            { "Liam (Vacation)", "Liam" },
            { "Mei (Vacation)", "Mei" },
            { "Michelle (Vacation)", "Michelle" },
            { "Riku (Vacation)", "Riku" },
            { "Sakura (Vacation)", "Sakura" },
            { "Sora (Vacation)", "Sora" },
            { "Toshiro (Vacation)", "Toshiro" },
            { "Wolfgirl (Vacation)", "Wolfgirl" },
            { "Wolfguy (Vacation)", "Wolfguy" },
        };

        /// <summary>What the game calls whoever a bust group describes.</summary>
        public static string SpokenName(string grouped)
        {
            string said;
            if (grouped != null && SpokenNames.TryGetValue(grouped, out said)) return said;
            return grouped ?? "";
        }

        /// <summary>
        /// A character's key, from its name. Lower case, letters and digits
        /// only - "Doctor Frost" and "Himari (Father)" are display names, not
        /// identifiers.
        /// </summary>
        public static string KeyFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var built = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) built.Append(char.ToLowerInvariant(c));
            return built.ToString();
        }
    }
}
