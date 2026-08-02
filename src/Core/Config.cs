using System;
using System.IO;
using System.Runtime.Serialization;
using System.Windows.Forms;
using GTA;
using OnTheBlade.Persistence;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Player-tunable settings. Written to scripts/OnTheBlade/config.json on
    /// first run so balance can be changed without a rebuild.
    /// </summary>
    [DataContract]
    public class Config
    {
        public static Config Current { get; private set; } = new Config();

        // F5 and F1 because the common GTA modding stack already claims most
        // F-keys: F3 Simple Trainer, F4 SHVDN console, F7 PSRP, F8 Menyoo.
        // F12 is left alone because Steam uses it for screenshots.
        [DataMember] public string MenuKeyName = "F5";
        [DataMember] public string PhoneKeyName = "F1";

        /// <summary>
        /// Adds an "On The Blade" contact to the in-game phone via iFruitAddon2.
        /// Turning this off skips loading that assembly entirely — see PhoneBridge.
        /// </summary>
        [DataMember] public bool EnablePhoneContact = true;

        // --- Streaming --------------------------------------------------
        // Workers exist as data at all times; a real Ped is only instantiated
        // inside SpawnRadius. Keep the gap wide enough that walking the
        // boundary does not thrash spawn/despawn.
        [DataMember] public float SpawnRadius = 150f;
        [DataMember] public float DespawnRadius = 220f;
        [DataMember] public int StreamScanIntervalMs = 750;

        // --- Economy ----------------------------------------------------
        // Base payout per worker per in-game hour, indexed by tier (1-3).
        [DataMember] public int TierBaseRate1 = 120;
        [DataMember] public int TierBaseRate2 = 260;
        [DataMember] public int TierBaseRate3 = 550;

        [DataMember] public float NightDemandBonus = 1.6f;   // 20:00 - 05:00
        [DataMember] public float StaminaDrainPerHour = 9f;
        [DataMember] public float StaminaRecoverPerHour = 14f;
        [DataMember] public float LoyaltyDrainWhenExhausted = 4f;
        [DataMember] public float HeatDecayPerHour = 0.03f;

        // --- Incidents --------------------------------------------------
        // Rolled once per in-game hour, at most one incident at a time.
        [DataMember] public float BadClientChance = 0.08f;       // per worker
        [DataMember] public float WalkOffChance = 0.35f;         // per worker below 25 loyalty
        [DataMember] public float ViceStingHeatFactor = 0.25f;   // chance = zoneHeat * this
        [DataMember] public int IncidentCooldownMs = 90000;      // quiet period after one ends

        // --- Territory ----------------------------------------------------
        [DataMember] public float RivalContestChance = 0.12f;    // x crew aggression, per hour
        [DataMember] public float OwnedZoneBonus = 1.15f;        // payout multiplier on held turf

        // --- Revenue: subscriptions ---------------------------------------
        // The second stream. Deliberately a parody name rather than a real
        // platform's — GTA's own brands are all parodies, and shipping a real
        // trademark in a public mod invites a takedown. Change it freely.
        [DataMember] public string SubscriptionBrand = "JustFans";

        [DataMember] public int SubscriptionPayoutDays = 7;

        /// <summary>Followers gained per hour off duty, before stamina/loyalty/tier scaling.</summary>
        [DataMember] public float FollowerGainPerHourOffDuty = 60f;

        /// <summary>Followers lost per hour on the street — nobody is posting.</summary>
        [DataMember] public float FollowerDecayPerHourWorking = 8f;

        // Tuned by simulating 12 game weeks for a tier-2 worker at 70 loyalty.
        // Street-only ~$16.9k/wk, subscriptions-only ~$8.0k/wk, and a night-shift
        // split ~$20.3k/wk — the mixed strategy wins, which is the point.
        [DataMember] public float RevenuePerFollowerWeekly = 0.5f;
        [DataMember] public float MaxFollowers = 25000f;

        // --- Recruiting ---------------------------------------------------
        /// <summary>
        /// Ped models that can be signed. This list is the eligibility filter —
        /// widen it here rather than in code if you want a bigger pool.
        /// </summary>
        [DataMember] public string[] RecruitModels = DefaultRecruitModels();

        /// <summary>
        /// Widened twice after play. Seven models found nobody at all; thirty-one
        /// still failed on many streets because ambient female models are heavily
        /// region-locked — soucent spawns in South Central, vinewood in Vinewood,
        /// ktown in Koreatown — so only a handful of any short list is plausible
        /// wherever you happen to be standing.
        ///
        /// This covers the ambient female population across every district plus
        /// the service and nightlife models. A name the game does not recognise
        /// simply never matches, so breadth costs nothing.
        /// </summary>
        public static string[] DefaultRecruitModels() => new[]
        {
            // Street and nightlife
            "s_f_y_hooker_01", "s_f_y_hooker_02", "s_f_y_hooker_03",
            "s_f_y_stripper_01", "s_f_y_stripper_02", "s_f_y_stripperlite",
            "s_f_y_bartender_01", "s_f_y_baywatch_01", "s_f_y_movprem_01",
            "s_f_y_shop_low", "s_f_y_shop_mid", "s_f_y_shop_high",
            "s_f_m_shop_high", "s_f_y_factory_01", "s_f_y_sweatshop_01",
            "s_f_m_sweatshop_01", "s_f_y_migrant_01", "s_f_m_fembarber",
            "s_f_m_maid_01",

            // Young adult, by district
            "a_f_y_hipster_01", "a_f_y_hipster_02", "a_f_y_hipster_03", "a_f_y_hipster_04",
            "a_f_y_business_01", "a_f_y_business_02", "a_f_y_business_03", "a_f_y_business_04",
            "a_f_y_bevhills_01", "a_f_y_bevhills_02", "a_f_y_bevhills_03", "a_f_y_bevhills_04",
            "a_f_y_vinewood_01", "a_f_y_vinewood_02", "a_f_y_vinewood_03", "a_f_y_vinewood_04",
            "a_f_y_soucent_01", "a_f_y_soucent_02", "a_f_y_soucent_03",
            "a_f_y_eastsa_01", "a_f_y_eastsa_02", "a_f_y_eastsa_03",
            "a_f_y_ktown_01", "a_f_y_ktown_02",
            "a_f_y_beachvesp_01", "a_f_y_beachvesp_02",
            "a_f_y_tourist_01", "a_f_y_tourist_02",
            "a_f_y_fitness_01", "a_f_y_fitness_02",
            "a_f_y_genhot_01", "a_f_y_yoga_01", "a_f_y_runner_01", "a_f_y_hiker_01",
            "a_f_y_golfer_01", "a_f_y_skater_01", "a_f_y_indian_01", "a_f_y_juggalo_01",
            "a_f_y_epsilon_01", "a_f_y_scdressy_01", "a_f_y_topless_01",
            "a_f_y_bodybuild_01", "a_f_y_rurmeth_01",

            // Middle-aged, by district
            "a_f_m_bevhills_01", "a_f_m_bevhills_02",
            "a_f_m_business_02", "a_f_m_downtown_01",
            "a_f_m_eastsa_01", "a_f_m_eastsa_02",
            "a_f_m_soucent_01", "a_f_m_soucent_02", "a_f_m_soucentmc_01",
            "a_f_m_ktown_01", "a_f_m_tourist_01", "a_f_m_salton_01",
            "a_f_m_fatcult_01", "a_f_m_fatwhite_01", "a_f_m_prolhost_01",
            "a_f_m_skidrow_01", "a_f_m_tramp_01",

            // Unique ambient
            "u_f_y_comjane", "u_f_y_hotposh_01", "u_f_y_jewelass_01",
            "u_f_y_mistress", "u_f_y_poppymich", "u_f_y_princess", "u_f_y_spyactress"
        };

        // Both raised after testing: 20m found nothing on a real street, and a
        // sweep you have to stand on top of someone for is not a mechanic.
        [DataMember] public float RecruitRadius = 35f;

        /// <summary>Wider sweep on gang turf and around Vinewood.</summary>
        [DataMember] public float RecruitRadiusHotspot = 70f;

        /// <summary>
        /// Allow signing women another script is already managing.
        ///
        /// Off by default because recruiting deletes the source ped, and deleting
        /// a ped that another mod still holds a handle to can break that mod. On
        /// an install running gang scripts this is the filter most likely to be
        /// hiding candidates — the recruit menu reports how many it rejected, so
        /// turn it on only if that count is doing real damage.
        /// </summary>
        [DataMember] public bool AllowPedsOwnedByOtherScripts;

        /// <summary>Blip nearby prospects while standing in a hotspot.</summary>
        [DataMember] public bool ShowProspectBlips = true;
        [DataMember] public int MaxProspectBlips = 8;
        [DataMember] public int ProspectBlipRefreshMs = 1500;

        // --- Poaching -----------------------------------------------------
        // Odds that a prospect is already working for somebody. Highest on gang
        // turf, because that is whose corner you are standing on.
        [DataMember] public float ClaimChanceGang = 0.45f;
        [DataMember] public float ClaimChanceVinewood = 0.30f;
        [DataMember] public float ClaimChanceOrdinary = 0.12f;

        /// <summary>Chance a claim brings her crew straight to you.</summary>
        [DataMember] public float RetaliationChance = 0.55f;

        // Modelled against the turf-defence roller: with no poaching a contest
        // lands roughly every 9 real minutes, rising to about every 5.6 after ten
        // poaches. It cannot spiral because Aggression clamps at 1.
        /// <summary>Aggression added to the crew you took her from.</summary>
        [DataMember] public float PoachAggressionHit = 0.15f;

        /// <summary>Aggression added to every other crew — word travels.</summary>
        [DataMember] public float PoachAggressionSpread = 0.04f;

        /// <summary>She has been through something and does not trust you yet.</summary>
        [DataMember] public float PoachLoyaltyPenalty = 15f;

        // --- Protection deals -----------------------------------------------
        /// <summary>Base price of peace, before strength, aggression and reputation.</summary>
        [DataMember] public int ProtectionBaseCost = 15000;
        [DataMember] public int ProtectionDays = 5;

        // --- Reputation -------------------------------------------------------
        [DataMember] public bool ReputationEnabled = true;

        // --- Demand events ----------------------------------------------------
        /// <summary>Chance per in-game hour of a new event when none is running.</summary>
        [DataMember] public float DemandEventChance = 0.04f;

        // --- Zone blips -----------------------------------------------------
        /// <summary>Draw territory on the map: green yours, red rival, grey neutral, yellow raided.</summary>
        [DataMember] public bool ShowZoneBlips = true;

        /// <summary>
        /// Shade the ground as well as pinning it.
        ///
        /// Off by default: a filled radius sits *under* every other blip but
        /// *over* the map itself, so on an install already carrying two or three
        /// turf overlays it hides roads and landmarks rather than adding
        /// information. The pins alone answer "where is my territory".
        /// </summary>
        [DataMember] public bool ShowZoneAreaCircles;

        /// <summary>0-255, and low on purpose. Only applies to the shaded ground.</summary>
        [DataMember] public int ZoneBlipAlpha = 45;

        // Territory is marked in pale colours so it sits behind the map rather
        // than on top of it. The game exposes a fixed palette, not arbitrary RGB,
        // so "Pink" and "GreyLight" are the lightest options available — there is
        // no paler pink to pick. Any GTA.BlipColor name works; unknown values
        // fall back to the default.
        [DataMember] public string ZoneColourMine = "Green2";
        [DataMember] public string ZoneColourRival = "Pink";
        [DataMember] public string ZoneColourNeutral = "GreyLight";
        [DataMember] public string ZoneColourRaided = "Yellow2";

        /// <summary>Zone pins sit below crew blips in the visual hierarchy.</summary>
        [DataMember] public float ZoneBlipScale = 0.6f;

        [DataMember] public int ZoneBlipRefreshMs = 3000;

        // --- Blip colours -------------------------------------------------
        // Any GTA.BlipColor name. Crew and prospects are told apart by blip size
        // and label rather than colour, so both can share one.
        [DataMember] public string WorkerBlipColourName = "Pink";
        [DataMember] public string ProspectBlipColourName = "Pink";

        // --- Business -----------------------------------------------------
        [DataMember] public int BaseRosterCap = 4;               // +2 per roster upgrade
        [DataMember] public int EnforcerHireCost = 12000;
        [DataMember] public int EnforcerDailyWage = 350;         // charged at 00:00 game time

        /// <summary>
        /// How much of an enforcer's skill counts toward waving a rival off turf
        /// in their region. Deterrence chance is skill/100 x this, so a good
        /// enforcer stops roughly half the attempts on ground they cover — enough
        /// to be worth the wage, not enough to make holding turf automatic.
        /// </summary>
        [DataMember] public float MuscleTurfDeterrence = 0.6f;
        [DataMember] public float StashStaminaBonus = 1.5f;      // off-duty recovery, any stash owned
        [DataMember] public float StashHeatDecayBonus = 2.0f;    // in that stash's region
        [DataMember] public float LaunderedHeatDecayBonus = 1.5f;

        // --- Upgrade effects -------------------------------------------------
        /// <summary>Extra seconds on every incident with the burner network.</summary>
        [DataMember] public int BurnerExtraSeconds = 30;

        /// <summary>Share of the weekly deposit the laundromat washes.</summary>
        [DataMember] public float LaundromatWashShare = 0.40f;

        /// <summary>
        /// Heat removed from each corner you hold when a washed deposit lands.
        /// This is what "heat stops following the money home" actually does.
        /// </summary>
        [DataMember] public float LaundromatHeatWash = 0.20f;

        /// <summary>Follower multiplier for Camera-ready workers once the ring light is bought.</summary>
        [DataMember] public float RingLightCameraReadyBonus = 1.5f;

        // --- Saturation ---------------------------------------------------
        // Per-worker yield on a zone is divided by 1 + (others * falloff).
        // Without this, stacking everyone on the highest-demand zone is strictly
        // optimal and the whole territory layer collapses into one corner.
        // At 0.35: 1 worker = 1.00 total, 2 = 1.48, 3 = 1.77, 4 = 1.95.
        [DataMember] public float ZoneSaturationFalloff = 0.35f;

        // --- Heat: raids ---------------------------------------------------
        [DataMember] public float RaidHeatThreshold = 0.95f;
        [DataMember] public int RaidLockoutDays = 3;
        [DataMember] public float RaidHeatAfter = 0.5f;
        [DataMember] public int RaidFine = 5000;

        // --- Bribes ---------------------------------------------------------
        /// <summary>Cost to clear one full point of heat on a zone.</summary>
        [DataMember] public int BribeCostPerHeat = 9000;
        [DataMember] public float BribeHeatCleared = 0.5f;

        // --- Debt and collapse ----------------------------------------------
        // Unpaid wages, bail and retention become debt instead of being waived.
        // Money used to be one-directional, so nothing could ever end a run.
        [DataMember] public float DebtInterestPerDay = 0.08f;
        [DataMember] public int DebtCollapseThreshold = 150000;

        // --- Vehicles --------------------------------------------------------
        [DataMember] public int VehicleCost = 28000;
        [DataMember] public float VehicleDemandBonus = 1.10f;
        [DataMember] public float VehicleStaminaRelief = 0.75f;

        /// <summary>
        /// Writes every worker spawn to the log with the post position, the sidewalk
        /// position the game snapped it to, and the distance between them. A large
        /// snap distance means the zone anchor is nowhere near a pavement.
        /// </summary>
        [DataMember] public bool LogSpawnDiagnostics;

        /// <summary>Adds a Diagnostics submenu for verifying zone anchors in-game.</summary>
        [DataMember] public bool ShowDiagnosticsMenu;

        [DataMember] public int AutoSaveIntervalMs = 120000;

        [IgnoreDataMember]
        public Keys MenuKey => ParseKey(MenuKeyName, Keys.F5);

        [IgnoreDataMember]
        public Keys PhoneKey => ParseKey(PhoneKeyName, Keys.F1);

        [IgnoreDataMember]
        public BlipColor WorkerBlipColour => ParseBlipColour(WorkerBlipColourName);

        [IgnoreDataMember]
        public BlipColor ProspectBlipColour => ParseBlipColour(ProspectBlipColourName);

        [IgnoreDataMember]
        public BlipColor ZoneMine => ParseBlipColour(ZoneColourMine, BlipColor.Green2);

        [IgnoreDataMember]
        public BlipColor ZoneRival => ParseBlipColour(ZoneColourRival, BlipColor.Pink);

        [IgnoreDataMember]
        public BlipColor ZoneNeutral => ParseBlipColour(ZoneColourNeutral, BlipColor.GreyLight);

        [IgnoreDataMember]
        public BlipColor ZoneRaided => ParseBlipColour(ZoneColourRaided, BlipColor.Yellow2);

        private static Keys ParseKey(string name, Keys fallback)
        {
            Keys parsed;
            return Enum.TryParse(name, true, out parsed) ? parsed : fallback;
        }

        /// <summary>A config.json written before this field existed deserialises it as null.</summary>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (RecruitModels == null || RecruitModels.Length == 0)
                RecruitModels = DefaultRecruitModels();
        }

        private static BlipColor ParseBlipColour(string name, BlipColor fallback = BlipColor.Pink)
        {
            BlipColor parsed;
            return Enum.TryParse(name, true, out parsed) ? parsed : fallback;
        }

        public int BaseRateFor(int tier)
        {
            switch (tier)
            {
                case 3: return TierBaseRate3;
                case 2: return TierBaseRate2;
                default: return TierBaseRate1;
            }
        }

        public static string Path => System.IO.Path.Combine(SaveManager.DataDirectory, "config.json");

        public static void Load()
        {
            if (File.Exists(Path))
            {
                var loaded = SaveManager.ReadJson<Config>(Path);
                if (loaded != null)
                {
                    Current = loaded;
                    return;
                }
            }

            Current = new Config();
            SaveManager.WriteJson(Path, Current);
        }
    }
}
