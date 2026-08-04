using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using System.Xml;
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

        // --- Tier progression ---------------------------------------------
        // Tier is the largest single term in the payout formula and it used to be
        // decided by one roll at recruit that the player could never touch. These
        // are the two ways to move it: work the hours, or pay for the step up.
        //
        // An in-game hour is roughly two real minutes at the default timescale.
        // On nights-only — the shift the mod nudges you toward — 30 hours is about
        // 2 real hours, so a first promotion lands inside a decent session. Tier 3
        // at 90 is a genuine long haul, which is what the wardrobe is there to
        // shortcut. Both were halved after simulating: the first pass put tier 3
        // at nearly 11 real hours of night shifts, which made the earned path
        // decorative rather than reachable.
        [DataMember] public int TierUpHours2 = 30;
        [DataMember] public int TierUpHours3 = 90;

        /// <summary>She has to want the step up. Below this, hours alone will not do it.</summary>
        [DataMember] public float TierUpLoyalty = 65f;

        // --- Per-worker investment -----------------------------------------
        // The money sink the mod was missing. Everything else you can buy is
        // global; these attach to one person and are lost if she leaves, which is
        // the point — it makes the roster something you own rather than cycle.
        //
        // Priced against the earned path rather than against the payout, because
        // an upgrade you can simply wait out for free is not a decision. At the
        // first pass $12,000 bought a tier that paid back over 60 street hours
        // while working 48 got it for nothing — strictly worse than doing nothing.
        // These land both steps at roughly break-even: short on time, you buy;
        // short on cash, you wait.
        [DataMember] public int WardrobeCost2 = 6000;
        [DataMember] public int WardrobeCost3 = 35000;

        /// <summary>You cannot buy a stranger to the top. Loyalty has to be there first.</summary>
        [DataMember] public float WardrobeMinLoyalty = 60f;

        // More loyalty than a plain bonus and a full night's rest with it, at
        // three times the price. Without the extra loyalty the bonus was simply
        // the better buy and this was never worth opening.
        [DataMember] public int ClinicCost = 1500;
        [DataMember] public float ClinicLoyalty = 15f;

        // Pays for itself in under two game weeks on a worker near the follower
        // cap. At $9,000 for +25% it took nearly thirty real hours, which is not
        // an investment so much as a donation.
        [DataMember] public int StudioCost = 4500;
        [DataMember] public float StudioFollowerBonus = 1.35f;

        /// <summary>Buys the Connected trait for one worker — she stops drawing heat.</summary>
        [DataMember] public int PapersCost = 6500;

        // --- Money: borrowing, pricing and collectors -------------------------
        /// <summary>
        /// What you owe for every dollar borrowed. Deliberately brutal — this is
        /// not a bank, and the money goes straight onto the debt the collapse
        /// system already watches.
        /// </summary>
        [DataMember] public float LoanMultiplier = 1.45f;

        /// <summary>Smallest and largest sums anyone will lend you.</summary>
        [DataMember] public int LoanMin = 10000;
        [DataMember] public int LoanMax = 120000;

        /// <summary>
        /// Ceiling on borrowing, as a share of the collapse threshold. Nobody will
        /// hand you enough rope to end the run in a single transaction.
        /// </summary>
        [DataMember] public float LoanDebtCeiling = 0.6f;

        // Pricing. Standard is 1x everything; the two ends trade income now
        // against a client base later, and shift how many people a corner can
        // usefully carry.
        //
        // The saturation figures are what make this a choice at all. At the first
        // pass premium out-earned the going rate at every headcount while also
        // cutting heat and trouble — the only thing it cost was regulars, which
        // made it very nearly a free upgrade. These are tuned so the two ends
        // cross over: premium wins a thin corner, cut price wins a full one.
        //
        //   workers      1     2     3     4      (relative corner yield)
        //   cut       0.80  1.38  1.83  2.17
        //   going     1.00  1.48  1.76  1.95
        //   premium   1.30  1.53  1.62  1.68
        [DataMember] public float CutPricePayout = 0.80f;
        [DataMember] public float CutPriceHeat = 1.40f;
        [DataMember] public float CutPriceRegulars = 2.00f;
        [DataMember] public float CutPriceSaturation = 0.45f;
        [DataMember] public float CutPriceTrouble = 1.35f;

        [DataMember] public float PremiumPayout = 1.30f;
        [DataMember] public float PremiumHeat = 0.70f;
        [DataMember] public float PremiumRegulars = 0.40f;
        [DataMember] public float PremiumSaturation = 2.00f;
        [DataMember] public float PremiumTrouble = 0.85f;

        // Collectors
        /// <summary>Share of the collapse threshold at which people start coming for it.</summary>
        [DataMember] public float CollectorsDebtShare = 0.55f;

        [DataMember] public float CollectorsChancePerDay = 0.35f;

        /// <summary>Days of interest they waive if you see them off.</summary>
        [DataMember] public int CollectorsGraceDays = 3;

        /// <summary>Share of the debt written off when they take somebody instead.</summary>
        [DataMember] public float CollectorsTakeShare = 0.35f;

        // --- Rivals: poaching back, war and alliances -------------------------
        /// <summary>
        /// Daily chance a crew tries to take somebody off you. Poaching was
        /// entirely one-directional until now, which made loyalty a payout
        /// multiplier and nothing else.
        /// </summary>
        [DataMember] public float RivalPoachChancePerDay = 0.05f;

        /// <summary>Loyalty at or above which nobody is going anywhere.</summary>
        [DataMember] public float RivalPoachSafeLoyalty = 70f;

        /// <summary>Loyalty she gains for turning them down.</summary>
        [DataMember] public float RivalPoachRefusedLoyalty = 10f;

        /// <summary>Strength a crew gains for taking one of yours.</summary>
        [DataMember] public float RivalPoachStrengthGain = 8f;

        /// <summary>How much armed muscle covering her ground puts them off.</summary>
        [DataMember] public float RivalPoachMuscleDeterrence = 0.6f;

        // War
        /// <summary>Aggression at which a crew stops contesting and starts a war.</summary>
        [DataMember] public float WarAggressionThreshold = 0.85f;

        /// <summary>Contest odds multiplier while at war.</summary>
        [DataMember] public float WarContestMultiplier = 2.2f;

        /// <summary>Extra bodies they bring to a turf battle while at war.</summary>
        [DataMember] public int WarExtraEnforcers = 2;

        /// <summary>Strength at which they give it up and sue for peace.</summary>
        [DataMember] public float WarEndStrength = 18f;

        /// <summary>
        /// Days after which a war burns out regardless of who is winning.
        ///
        /// Without this a war has exactly one exit that does not require winning:
        /// paying the premium. But losing a defence <em>raises</em> their strength,
        /// so a player who cannot win turf battles and cannot afford the buyout
        /// would be stuck being attacked every few minutes indefinitely, with the
        /// condition that ends it moving further away every time they lose.
        /// </summary>
        [DataMember] public int WarMaxDays = 14;

        /// <summary>Buying out of a war costs this many times the ordinary price.</summary>
        [DataMember] public float WarTrucePremium = 3.0f;

        // Alliances
        /// <summary>Reputation before a crew will stand with you rather than merely leave you alone.</summary>
        [DataMember] public int AllianceMinReputation = 400;

        /// <summary>Alliance price, as a multiple of the protection price.</summary>
        [DataMember] public float AllianceCostMultiplier = 3.5f;

        [DataMember] public int AllianceDays = 10;

        /// <summary>Bodies an allied crew sends to a turf battle on your side.</summary>
        [DataMember] public int AllySpawnCount = 3;

        // --- The law ----------------------------------------------------------
        /// <summary>A man at the station, bought a week at a time.</summary>
        [DataMember] public int CopRetainerDailyCost = 2500;
        [DataMember] public int CopRetainerDays = 7;

        /// <summary>In-game hours of notice before a raid he could not stop.</summary>
        [DataMember] public int CopWarningHours = 6;

        /// <summary>Roster size before anyone is worth turning.</summary>
        [DataMember] public int InformantMinRoster = 4;

        /// <summary>Nobody loyal is talking to the police.</summary>
        [DataMember] public float InformantMaxLoyalty = 55f;

        [DataMember] public float InformantChancePerDay = 0.012f;

        /// <summary>
        /// Heat she generates, multiplied. This is the only tell before the hint.
        ///
        /// Cut from 2.2 after simulating against the hottest corner: on Strawberry
        /// a single informant took the zone from cold to raided in 26 in-game
        /// hours, while the hint did not arrive until 48. The corner was gone
        /// before the player was told there was anything to find, which is not a
        /// mystery, it is an ambush. At 1.6 the same corner takes about 53 hours
        /// and the tell lands at 24.
        /// </summary>
        [DataMember] public float InformantHeatMultiplier = 1.6f;

        /// <summary>
        /// Retired. You are told the moment somebody starts talking — see
        /// <see cref="Systems.Law.RollInformant"/> for why the delay had to go.
        /// Kept so an existing config.json does not lose a key it already has.
        /// </summary>
        [DataMember] public int InformantHintDays;

        /// <summary>Cost of finding out who. Halved by the legal retainer.</summary>
        [DataMember] public int InformantSweepCost = 14000;

        [DataMember] public float AccusationWrongLoyaltyHit = 20f;
        [DataMember] public int AccusationRightReputation = 25;
        [DataMember] public int AccusationWrongReputation = 35;

        // --- Custody ----------------------------------------------------------
        // A bust used to be a fine and nothing else. Now she goes away, which is
        // the only thing that makes vice frightening rather than expensive.
        [DataMember] public int CustodyDaysMin = 2;
        [DataMember] public int CustodyDaysMax = 6;
        [DataMember] public int CourtFine = 4000;

        /// <summary>Per remaining day. Halved by the legal retainer.</summary>
        [DataMember] public int LawyerCostPerDay = 2200;

        [DataMember] public float LawyerLoyaltyReward = 18f;
        [DataMember] public float CustodyLoyaltyHit = 20f;

        // --- Crew: managers, mentors and leaving ------------------------------
        /// <summary>
        /// Tier needed to run a corner instead of working it. Set to 3 because
        /// tier 3 previously had nowhere left to go — promotion topped out and the
        /// stat stopped meaning anything.
        /// </summary>
        [DataMember] public int ManagerMinTier = 3;

        /// <summary>
        /// Payout multiplier for everyone else working a managed corner.
        ///
        /// Raised from 1.30 after simulating the actual trade. A tier-3 gives up a
        /// large personal income to run a corner, and at 30% the three juniors
        /// left on it never made that back — running it lost to working it at
        /// every loyalty level, which made the whole promotion a trap.
        ///
        /// At 1.75 it turns over: clearly worth it with a loyal manager, roughly
        /// even in the middle, and a loss if she has checked out. Which is the
        /// decision it was supposed to be.
        /// </summary>
        [DataMember] public float ManagerPayoutBonus = 1.75f;

        /// <summary>Heat multiplier on a managed corner. Someone is watching the door.</summary>
        [DataMember] public float ManagerHeatReduction = 0.65f;

        /// <summary>
        /// Extra experience hours a junior gains per hour worked alongside someone
        /// of a higher tier. Makes who you post together a decision rather than a
        /// sum of individual stats.
        /// </summary>
        [DataMember] public int MentorBonusHours = 1;

        /// <summary>Lifetime hours before she starts thinking about getting out.</summary>
        [DataMember] public int BurnoutHours = 400;

        /// <summary>Daily chance of asking to leave, once past that.</summary>
        [DataMember] public float BurnoutChancePerDay = 0.12f;

        /// <summary>
        /// Daily chance of wanting out for reasons that have nothing to do with
        /// you. Per worker, so it multiplies across the roster — at 1.5% a book of
        /// nine produced somebody resigning every week, which turns a moment into
        /// paperwork.
        /// </summary>
        [DataMember] public float PersonalExitChancePerDay = 0.008f;

        /// <summary>Days to answer before she goes on her own and takes your name with her.</summary>
        [DataMember] public int ExitDecisionDays = 3;

        /// <summary>Cost to talk her round, per tier.</summary>
        [DataMember] public int RetentionCostPerTier = 9000;

        /// <summary>Loyalty restored when you pay her to stay.</summary>
        [DataMember] public float RetentionLoyalty = 25f;

        /// <summary>Loyalty lost when you simply refuse.</summary>
        [DataMember] public float RefusalLoyaltyHit = 30f;

        /// <summary>Reputation for letting someone go properly, and for not.</summary>
        [DataMember] public int ExitGoodReputation = 20;
        [DataMember] public int ExitBadReputation = 25;

        /// <summary>Chance a good exit sends somebody your way.</summary>
        [DataMember] public float ReferralChance = 0.6f;

        /// <summary>Loyalty a referred recruit starts with on top of the usual.</summary>
        [DataMember] public float ReferralLoyaltyBonus = 20f;

        // --- Clients ----------------------------------------------------------
        /// <summary>Regulars a worker can hold. Past this nobody new sticks.</summary>
        [DataMember] public int MaxRegulars = 12;

        /// <summary>Added per worked hour, per regular. Small on purpose — it stacks.</summary>
        [DataMember] public float RegularValuePerHour = 9f;

        /// <summary>Chance per worked hour that one more client starts coming back.</summary>
        [DataMember] public float RegularGainChance = 0.05f;

        /// <summary>
        /// Chance per game day that a benched worker loses a regular. A regular
        /// who turns up twice to nobody stops being one, and this is what stops
        /// the roster being freely reshuffled once it is built.
        /// </summary>
        [DataMember] public float RegularDecayChance = 0.30f;

        /// <summary>Chance per in-game hour that somebody asks for one of your crew.</summary>
        [DataMember] public float BookingOfferChance = 0.035f;

        /// <summary>Share of offers that come from somebody connected rather than rich.</summary>
        [DataMember] public float ConnectedOfferShare = 0.30f;

        /// <summary>An offer left unanswered this many in-game hours is withdrawn.</summary>
        [DataMember] public int BookingOfferExpiryHours = 6;

        /// <summary>How long she is unavailable once a booking is accepted.</summary>
        [DataMember] public int BookingHoursMin = 4;
        [DataMember] public int BookingHoursMax = 9;

        /// <summary>Payout is this many hours of her street rate, times appeal.</summary>
        [DataMember] public float BookingPayoutHours = 14f;

        /// <summary>Floor on how badly a booking can go, before money and traits.</summary>
        [DataMember] public float BookingBaseRisk = 0.10f;

        /// <summary>
        /// Every this many dollars of payout adds one full point of risk.
        ///
        /// Raised from 30,000 after simulating: at that value a tier-3 worker's
        /// booking priced out past the 85% clamp, so the appeal that made your
        /// best earners worth asking for also guaranteed the night went wrong —
        /// and a $31,000 booking carried exactly the same risk as a $46,000 one.
        /// The whole top of the curve was flat and unplayable.
        /// </summary>
        [DataMember] public int BookingRiskPerDollar = 90000;

        /// <summary>How much armed muscle covering her ground takes off the risk.</summary>
        [DataMember] public float BookingMuscleCover = 0.5f;

        /// <summary>Loyalty and reputation swings when a booking lands or goes wrong.</summary>
        [DataMember] public float BookingLoyaltyReward = 8f;
        [DataMember] public float BookingLoyaltyPenalty = 25f;
        [DataMember] public float BookingStaminaCost = 30f;

        /// <summary>Heat taken off every held corner by a vice contact's favour.</summary>
        [DataMember] public float LeverageHeatCleared = 0.45f;

        /// <summary>Days of peace bought by a fixer's favour.</summary>
        [DataMember] public int LeverageTruceDays = 7;

        /// <summary>Followers handed over by a producer's favour.</summary>
        [DataMember] public float LeverageFollowers = 6000f;

        // --- Houses ---------------------------------------------------------
        // The indoor operation. Deliberately not given the night bonus: the street
        // pays 1.6x after dark and a house pays the same around the clock, so the
        // same worker is worth more indoors by day and more on a corner by night.
        // That is the whole allocation decision, and handing the house both would
        // erase it.

        /// <summary>Indoor stamina drain, as a share of the street rate. She is not walking.</summary>
        [DataMember] public float HouseStaminaDrain = 0.55f;

        /// <summary>Heat bled off a house per hour. Slower than a corner — nothing airs out.</summary>
        [DataMember] public float HouseHeatDecayPerHour = 0.015f;

        /// <summary>
        /// A house is raided later than a corner but far harder. One night empties
        /// it, and the fine is meant to be felt against a six-figure purchase.
        /// </summary>
        [DataMember] public float HouseRaidHeatThreshold = 0.90f;
        [DataMember] public int HouseRaidLockoutDays = 5;
        [DataMember] public float HouseRaidHeatAfter = 0.35f;
        [DataMember] public int HouseRaidFine = 25000;

        /// <summary>Loyalty every worker in the house loses when it is turned over.</summary>
        [DataMember] public float HouseRaidLoyaltyHit = 18f;

        /// <summary>Draw a blip on each house you own.</summary>
        [DataMember] public bool ShowHouseBlips = true;
        [DataMember] public string HouseBlipColourName = "Purple";

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

        // --- Armed muscle ---------------------------------------------------
        /// <summary>
        /// An armed enforcer turns up in person at incidents in the region he
        /// covers. Turn this off to keep muscle purely abstract — the skill bonus
        /// from a weapon still applies either way.
        /// </summary>
        [DataMember] public bool MuscleTurnsUp = true;

        /// <summary>How close the player must be before he is spawned in.</summary>
        [DataMember] public float MuscleSpawnRange = 140f;

        /// <summary>Radius he will engage hostiles within.</summary>
        [DataMember] public float MuscleFightRadius = 80f;

        /// <summary>
        /// Game days off after being carried out of a fight. He stays on the
        /// payroll throughout, which is the cost of sending him in underarmed.
        /// </summary>
        [DataMember] public int MuscleInjuryDays = 2;

        /// <summary>
        /// Ceiling on resolving client trouble off-screen, as a share of effective
        /// skill. Below 1 so a well-armed enforcer still lets some through for you
        /// to watch him handle in person.
        /// </summary>
        [DataMember] public float MuscleClientHandleCap = 0.75f;

        /// <summary>Blip him on the map while he is out with you.</summary>
        [DataMember] public bool ShowMuscleBlip = true;

        [DataMember] public string MuscleBlipColourName = "Blue";

        /// <summary>
        /// Ped models an enforcer can turn up as. One is picked at hire and kept,
        /// so the same man arrives every time.
        /// </summary>
        [DataMember] public string[] EnforcerModels = DefaultEnforcerModels();

        public static string[] DefaultEnforcerModels() => new[]
        {
            "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01",
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01",
            "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03",
            "g_m_y_salvagoon_01", "g_m_y_salvagoon_02",
            "g_m_y_pologoon_01", "g_m_y_pologoon_02",
            "a_m_m_soucent_01", "a_m_m_soucent_02", "a_m_y_stlat_01",
            "s_m_y_dealer_01", "s_m_m_bouncer_01", "s_m_y_doorman_01"
        };
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

        [IgnoreDataMember]
        public BlipColor MuscleBlipColour => ParseBlipColour(MuscleBlipColourName, BlipColor.Blue);

        [IgnoreDataMember]
        public BlipColor HouseBlipColour => ParseBlipColour(HouseBlipColourName, BlipColor.Purple);

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

            if (EnforcerModels == null || EnforcerModels.Length == 0)
                EnforcerModels = DefaultEnforcerModels();
        }

        /// <summary>
        /// Restores any setting the file on disk does not mention.
        ///
        /// DataContractJsonSerializer builds the object with
        /// FormatterServices.GetUninitializedObject, which runs neither the
        /// constructor nor the field initialisers. So every key added after a
        /// player's config.json was written loads as 0 / false / null rather than
        /// as the default written next to it in this file — silently, and with no
        /// error anywhere.
        ///
        /// That is not a hypothetical. Adding tier progression against an existing
        /// config produced TierUpHours2 = 0 and TierUpLoyalty = 0, which promotes
        /// every worker to tier 3 within two in-game hours, and
        /// StudioFollowerBonus = 0, which multiplies follower gain by zero. Up to
        /// now the only thing hiding this was regenerating config.json by hand
        /// after every change.
        ///
        /// Missing keys are detected rather than guessed at, so a value the player
        /// deliberately set to 0 is left exactly as they set it.
        /// </summary>
        private bool RestoreMissingKeys(string path)
        {
            HashSet<string> present;
            try
            {
                present = TopLevelKeys(path);
            }
            catch
            {
                // A file we cannot parse for keys is one we should not second-guess.
                return false;
            }

            if (present.Count == 0) return false;

            var defaults = new Config();
            bool repaired = false;

            foreach (var field in typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetCustomAttributes(typeof(DataMemberAttribute), false).Length == 0) continue;
                if (present.Contains(field.Name)) continue;

                field.SetValue(this, field.GetValue(defaults));
                repaired = true;
            }

            return repaired;
        }

        /// <summary>
        /// The keys actually written in the file. JsonReaderWriterFactory maps a
        /// JSON object's keys onto XML element names, so the top level of the
        /// document is exactly the set of settings present — no string matching,
        /// and no third-party JSON library.
        /// </summary>
        private static HashSet<string> TopLevelKeys(string path)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            byte[] bytes = File.ReadAllBytes(path);

            using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                       bytes, XmlDictionaryReaderQuotas.Max))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                        names.Add(reader.LocalName);
                }
            }

            return names;
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

        /// <summary>Street hours needed to earn promotion <em>into</em> this tier.</summary>
        public int TierUpHoursFor(int targetTier)
        {
            return targetTier >= 3 ? TierUpHours3 : TierUpHours2;
        }

        /// <summary>Price of buying promotion <em>into</em> this tier.</summary>
        public int WardrobeCostFor(int targetTier)
        {
            return targetTier >= 3 ? WardrobeCost3 : WardrobeCost2;
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

                    // Rewritten so the file gains the settings it was missing and
                    // the player can actually see and tune them, rather than
                    // having them silently repaired on every load forever.
                    if (loaded.RestoreMissingKeys(Path))
                    {
                        SaveManager.WriteJson(Path, Current);
                        SaveManager.Log("config.json was missing settings; defaults restored and file rewritten.");
                    }

                    return;
                }
            }

            Current = new Config();
            SaveManager.WriteJson(Path, Current);
        }
    }
}
