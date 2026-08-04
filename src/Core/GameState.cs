using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Everything that survives a save. Nothing in here may hold a GTA entity
    /// handle — handles are invalidated across sessions.
    /// </summary>
    [DataContract]
    public class GameState
    {
        /// <summary>
        /// Bump whenever the saved shape changes, and add a step to
        /// <see cref="Migrate"/>. Without this, a reshaped save fails silently
        /// and half-populated rather than loudly.
        /// </summary>
        public const int CurrentSaveVersion = 12;

        [DataMember] public int SaveVersion;

        public static GameState Current { get; set; } = new GameState();

        [DataMember] public int NextWorkerId = 1;
        [DataMember] public List<WorkerData> Roster = new List<WorkerData>();

        /// <summary>Zone id -> heat, 0..1.</summary>
        [DataMember] public Dictionary<string, float> ZoneHeat = new Dictionary<string, float>();

        /// <summary>Zone id -> owner id. Missing or empty means neutral.</summary>
        [DataMember] public Dictionary<string, string> ZoneOwners = new Dictionary<string, string>();

        [DataMember] public List<RivalCrew> Rivals = new List<RivalCrew>();

        /// <summary>Ids from <see cref="UpgradeCatalog"/>.</summary>
        [DataMember] public List<string> Upgrades = new List<string>();

        /// <summary>Region ids where a stash house has been bought.</summary>
        [DataMember] public List<string> StashHouses = new List<string>();

        /// <summary>Ids from <see cref="Houses"/> that have been bought.</summary>
        [DataMember] public List<string> OwnedHouses = new List<string>();

        /// <summary>House id -> heat, 0..1. Kept apart from zone heat: a house is
        /// not on anyone's corner and must not inherit the street's temperature.</summary>
        [DataMember] public Dictionary<string, float> HouseHeat = new Dictionary<string, float>();

        /// <summary>House id -> absolute day the raid shutdown expires.</summary>
        [DataMember] public Dictionary<string, int> HouseLockedUntilDay = new Dictionary<string, int>();

        /// <summary>Lifetime take earned indoors.</summary>
        [DataMember] public int LifetimeHouseTake;

        // --- the client currently asking, flattened so it serialises plainly ---
        // Only ever one at a time, same as the running demand event. Two offers
        // competing for the same worker would need a queue and a queue would make
        // this a to-do list, which is the thing the mod is trying not to be.
        [DataMember] public int OfferKind;
        [DataMember] public int OfferWorkerId = -1;
        [DataMember] public string OfferClientName;
        [DataMember] public int OfferPayout;
        [DataMember] public float OfferRisk;
        [DataMember] public int OfferLeverage;
        [DataMember] public int OfferHours;
        [DataMember] public int OfferExpiresAtHour;

        [IgnoreDataMember]
        public bool HasOffer => OfferKind != (int)ClientKind.None && OfferWorkerId >= 0;

        /// <summary>Lifetime take from bookings.</summary>
        [DataMember] public int LifetimeBookingTake;

        /// <summary>
        /// Signings owed to you by people who left on good terms. Consumed one at
        /// a time by the next recruit, who arrives already trusting you.
        /// </summary>
        [DataMember] public int PendingReferrals;

        // --- the law ---
        /// <summary>Absolute day the police retainer runs out.</summary>
        [DataMember] public int CopPaidUntilDay;

        /// <summary>How many times you have let him go. He charges more each time.</summary>
        [DataMember] public int CopBurnedCount;

        /// <summary>Zone he has warned you about, and how long the notice lasts.</summary>
        [DataMember] public string WarnedZoneId;
        [DataMember] public int WarningExpiresAtHour;

        /// <summary>Worker quietly talking to the police. -1 for nobody.</summary>
        [DataMember] public int InformantWorkerId = -1;
        [DataMember] public int InformantSinceDay;

        /// <summary>Whether you have been told somebody is talking.</summary>
        [DataMember] public bool InformantHinted;

        /// <summary>Whether a sweep has actually named her.</summary>
        [DataMember] public bool InformantKnown;

        // --- money ---
        /// <summary>Zone id -> <see cref="PriceLevel"/>. Missing means the going rate.</summary>
        [DataMember] public Dictionary<string, int> ZonePrices = new Dictionary<string, int>();

        /// <summary>Cash borrowed over the run, before interest.</summary>
        [DataMember] public int LifetimeBorrowed;

        /// <summary>Absolute day collectors will leave you alone until.</summary>
        [DataMember] public int CollectorsGraceUntilDay;

        /// <summary>
        /// Somebody is on their way. Set by the daily roll and consumed by the
        /// incident roller, because the daily tick runs at midnight and an
        /// incident has to start on an hour the player is actually playing.
        /// </summary>
        [DataMember] public bool CollectorsPending;

        public void ClearOffer()
        {
            OfferKind = (int)ClientKind.None;
            OfferWorkerId = -1;
            OfferClientName = null;
            OfferPayout = 0;
            OfferRisk = 0f;
            OfferLeverage = (int)LeverageKind.None;
            OfferHours = 0;
            OfferExpiresAtHour = 0;
        }

        [DataMember] public List<EnforcerData> Enforcers = new List<EnforcerData>();
        [DataMember] public int NextEnforcerId = 1;

        /// <summary>How many crew you have taken off other people.</summary>
        [DataMember] public int PoachedCount;

        /// <summary>Region ids where a vehicle has been bought.</summary>
        [DataMember] public List<string> Vehicles = new List<string>();

        /// <summary>Ids from <see cref="MilestoneCatalog"/> already awarded.</summary>
        [DataMember] public List<string> Milestones = new List<string>();

        /// <summary>
        /// Unpaid wages, bail and retention. Accrues interest daily and ends the
        /// operation if it runs away.
        /// </summary>
        [DataMember] public int Debt;

        [DataMember] public int TimesCollapsed;

        /// <summary>Zone id -> absolute day number the raid lockout expires.</summary>
        [DataMember] public Dictionary<string, int> ZoneLockedUntilDay = new Dictionary<string, int>();

        /// <summary>Rival id -> absolute day a paid truce runs out.</summary>
        [DataMember] public Dictionary<string, int> TruceUntilDay = new Dictionary<string, int>();

        /// <summary>0-1000. See <see cref="Reputation"/>.</summary>
        [DataMember] public int Reputation;

        // --- the running demand event, flattened so it serialises plainly ---
        [DataMember] public string EventKind;
        [DataMember] public string EventZoneId;
        [DataMember] public float EventMultiplier = 1f;
        [DataMember] public int EventEndsAtHour;

        /// <summary>
        /// Distinguishes a fresh save from one where the player has genuinely
        /// neutralised every zone — without it, clearing the board would reseed it.
        /// </summary>
        [DataMember] public bool TerritorySeeded;

        /// <summary>Grand total across both revenue streams.</summary>
        [DataMember] public int LifetimeTake;

        /// <summary>The subscription share of <see cref="LifetimeTake"/>. Street
        /// take is the remainder — storing one component keeps old saves valid.</summary>
        [DataMember] public int LifetimeSubscriptionTake;

        /// <summary>Anchor for the weekly deposit, stored as an ordinal date so it
        /// survives a save without depending on a running clock.</summary>
        [DataMember] public int LastPayoutYear = -1;
        [DataMember] public int LastPayoutDayOfYear = -1;

        [IgnoreDataMember]
        public int LifetimeStreetTake => LifetimeTake - LifetimeSubscriptionTake;

        /// <summary>Last in-game hour the economy resolved for, so a reload does
        /// not immediately re-run a tick.</summary>
        [DataMember] public int LastEconomyHour = -1;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx) => EnsureCollections();

        public void EnsureCollections()
        {
            if (Roster == null) Roster = new List<WorkerData>();
            if (ZoneHeat == null) ZoneHeat = new Dictionary<string, float>();
            if (ZoneOwners == null) ZoneOwners = new Dictionary<string, string>();
            if (Rivals == null) Rivals = new List<RivalCrew>();
            if (Upgrades == null) Upgrades = new List<string>();
            if (StashHouses == null) StashHouses = new List<string>();
            if (Enforcers == null) Enforcers = new List<EnforcerData>();
            if (Vehicles == null) Vehicles = new List<string>();
            if (Milestones == null) Milestones = new List<string>();
            if (ZoneLockedUntilDay == null) ZoneLockedUntilDay = new Dictionary<string, int>();
            if (TruceUntilDay == null) TruceUntilDay = new Dictionary<string, int>();
            if (OwnedHouses == null) OwnedHouses = new List<string>();
            if (HouseHeat == null) HouseHeat = new Dictionary<string, float>();
            if (HouseLockedUntilDay == null) HouseLockedUntilDay = new Dictionary<string, int>();
            if (ZonePrices == null) ZonePrices = new Dictionary<string, int>();

            Migrate();

            if (!TerritorySeeded)
            {
                Rivals = RivalCatalog.Defaults();
                ZoneOwners = RivalCatalog.DefaultOwnership();
                TerritorySeeded = true;
            }

            foreach (var w in Roster) w.Clamp();
            foreach (var r in Rivals) r.Clamp();
            foreach (var e in Enforcers) e.Clamp();
        }

        // --- upgrades, property, muscle -----------------------------------

        public bool HasUpgrade(string id) => Upgrades.Contains(id);

        public int RosterCap
        {
            get
            {
                int cap = Config.Current.BaseRosterCap;
                if (HasUpgrade(UpgradeCatalog.RosterA)) cap += 2;
                if (HasUpgrade(UpgradeCatalog.RosterB)) cap += 2;
                return cap;
            }
        }

        public bool RosterFull => Roster.Count >= RosterCap;

        public bool OwnsVehicle(string regionId) =>
            !string.IsNullOrEmpty(regionId) && Vehicles.Contains(regionId);

        /// <summary>True if the zone's region has a vehicle running.</summary>
        public bool ZoneHasVehicle(string zoneId) => OwnsVehicle(Regions.ForZone(zoneId)?.Id);

        public bool OwnsStash(string regionId) =>
            !string.IsNullOrEmpty(regionId) && StashHouses.Contains(regionId);

        /// <summary>True if the zone's region has a stash house.</summary>
        public bool ZoneHasStash(string zoneId) => OwnsStash(Regions.ForZone(zoneId)?.Id);

        /// <summary>The enforcer covering a zone's region, if any.</summary>
        public EnforcerData EnforcerFor(string zoneId)
        {
            var region = Regions.ForZone(zoneId);
            if (region == null) return null;
            return Enforcers.FirstOrDefault(e => e.RegionId == region.Id);
        }

        public int DailyWageBill => Enforcers.Sum(e => e.DailyWage);

        /// <summary>
        /// Steps run in order and are each idempotent, so a save can jump several
        /// versions at once. Version 0 means "written before versioning existed".
        /// </summary>
        private void Migrate()
        {
            if (SaveVersion >= CurrentSaveVersion) return;
            int from = SaveVersion;

            if (SaveVersion < 1)
            {
                // Models used to be stored by name only; WorkerData.Clamp back-fills
                // the hash, so nothing to do but record that we have been here.
                SaveVersion = 1;
            }

            if (SaveVersion < 2)
            {
                // Subscriptions arrived. Anchor the payout to today rather than
                // letting a null anchor pay out a backdated windfall.
                LastPayoutYear = -1;
                LastPayoutDayOfYear = -1;
                SaveVersion = 2;
            }

            if (SaveVersion < 3)
            {
                // Shifts arrived. Always == 0 already matches old behaviour; this
                // is explicit so the intent survives the next reader.
                foreach (var w in Roster) w.Shift = WorkerShift.Always;
                SaveVersion = 3;
            }

            if (SaveVersion < 4)
            {
                // Reputation, truces and demand events arrived. A save from before
                // them starts at zero rep with nothing running, which is correct —
                // but the event multiplier must not deserialise as 0 and silently
                // zero every payout.
                if (EventMultiplier <= 0f) EventMultiplier = 1f;
                EventKind = null;
                EventZoneId = null;
                SaveVersion = 4;
            }

            if (SaveVersion < 5)
            {
                // Tier progression and per-worker investment arrived. Both new
                // worker fields are deliberately shaped so their zero value is the
                // correct one — HoursWorked 0 means she starts earning experience
                // from now, HasStudio false means nothing was bought for her. So
                // there is nothing to back-fill, only a version to record.
                SaveVersion = 5;
            }

            if (SaveVersion < 6)
            {
                // Armed muscle arrived. Enforcers hired before it have no weapon
                // and no ped model — WeaponId deserialises as null, which IsArmed
                // already reads as unarmed, but the model has to be filled in or
                // they can never be spawned once you do arm them.
                var models = Config.Current.EnforcerModels;
                var rng = new System.Random();

                foreach (var e in Enforcers)
                {
                    if (string.IsNullOrEmpty(e.WeaponId)) e.WeaponId = Armoury.Unarmed;

                    if (string.IsNullOrEmpty(e.ModelName))
                        e.ModelName = models != null && models.Length > 0
                            ? models[rng.Next(models.Length)]
                            : "g_m_y_famca_01";
                }

                SaveVersion = 6;
            }

            if (SaveVersion < 7)
            {
                // Houses arrived. Nobody can already be indoors, and every new
                // collection defaults empty, so there is nothing to back-fill —
                // but HouseId must be cleared explicitly rather than trusted,
                // because a null string field and an empty one behave differently
                // in IsIndoors and this is cheaper than finding that out in play.
                foreach (var w in Roster) w.HouseId = null;
                SaveVersion = 7;
            }

            if (SaveVersion < 8)
            {
                // Clients arrived. OfferWorkerId defaults to -1 in a fresh object
                // but deserialises as 0 from a save that predates it, and 0 is a
                // plausible worker id — that would have the phone offering a
                // booking to whoever happened to be first on the books.
                ClearOffer();

                foreach (var w in Roster)
                {
                    w.BookingEndsAtHour = 0;
                    w.BookingPayout = 0;
                    w.BookingRisk = 0f;
                    w.BookingClientName = null;
                    w.BookingLeverage = (int)LeverageKind.None;
                }

                SaveVersion = 8;
            }

            if (SaveVersion < 9)
            {
                // Managers, mentors and leaving arrived. Seed LifetimeHours from
                // the hours already banked toward the current tier rather than
                // starting everyone at zero — an existing crew has visibly been
                // working, and resetting that would put burnout further away for
                // veterans than for someone signed this morning.
                foreach (var w in Roster)
                {
                    if (w.LifetimeHours <= 0) w.LifetimeHours = w.HoursWorked;
                    w.ManagesZoneId = null;
                    w.WantsOutReason = 0;
                    w.WantsOutSinceDay = 0;
                }

                SaveVersion = 9;
            }

            if (SaveVersion < 10)
            {
                // The law arrived. InformantWorkerId is the trap here: it defaults
                // to -1 on a fresh object but deserialises as 0 from an older save,
                // and 0 is a plausible worker id — the mod would have quietly
                // decided whoever was first on the books had been informing all
                // along, and doubled the heat they generate.
                InformantWorkerId = -1;
                InformantSinceDay = 0;
                InformantHinted = false;
                InformantKnown = false;

                CopPaidUntilDay = 0;
                CopBurnedCount = 0;
                WarnedZoneId = null;
                WarningExpiresAtHour = 0;

                foreach (var w in Roster) w.JailedUntilDay = 0;

                SaveVersion = 10;
            }

            if (SaveVersion < 11)
            {
                // War and alliances arrived. Both new crew fields are day numbers
                // where zero already means "not happening", so there is nothing to
                // back-fill — but an existing save can easily be carrying a crew
                // sitting above the war threshold, and they should have to cross
                // it in play rather than be at war the moment the save loads.
                foreach (var r in Rivals)
                {
                    r.WarSinceDay = 0;
                    r.AlliedUntilDay = 0;

                    if (r.Aggression >= Config.Current.WarAggressionThreshold)
                        r.Aggression = Config.Current.WarAggressionThreshold - 0.05f;
                }

                SaveVersion = 11;
            }

            if (SaveVersion < 12)
            {
                // Borrowing, pricing and collectors arrived. Every corner starts at
                // the going rate, which is what a missing entry already means, so
                // the dictionary is deliberately left empty rather than seeded.
                CollectorsGraceUntilDay = 0;
                SaveVersion = 12;
            }

            Persistence.SaveManager.Log($"Save migrated from version {from} to {SaveVersion}.");
        }

        // --- day helpers --------------------------------------------------

        /// <summary>
        /// Monotonic day number for lockouts and interest. Years are padded to 400
        /// so the value never goes backwards across a year boundary.
        /// </summary>
        public static int AbsoluteDay()
        {
            var today = GTA.Chrono.GameClock.Today;
            return today.Year * 400 + today.DayOfYear;
        }

        public bool IsZoneLocked(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return false;
            int until;
            if (!ZoneLockedUntilDay.TryGetValue(zoneId, out until)) return false;
            return AbsoluteDay() < until;
        }

        public int LockoutDaysLeft(string zoneId)
        {
            int until;
            if (!ZoneLockedUntilDay.TryGetValue(zoneId, out until)) return 0;
            int left = until - AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        /// <summary>Monotonic hour, for anything shorter-lived than a day.</summary>
        public static int AbsoluteHour() => AbsoluteDay() * 24 + GTA.Chrono.GameClock.Hour;

        // --- truces -------------------------------------------------------

        public bool HasTruce(string rivalId)
        {
            if (string.IsNullOrEmpty(rivalId)) return false;
            int until;
            if (!TruceUntilDay.TryGetValue(rivalId, out until)) return false;
            return AbsoluteDay() < until;
        }

        public int TruceDaysLeft(string rivalId)
        {
            int until;
            if (string.IsNullOrEmpty(rivalId) || !TruceUntilDay.TryGetValue(rivalId, out until)) return 0;
            int left = until - AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        public void SetTruce(string rivalId, int days)
        {
            if (string.IsNullOrEmpty(rivalId)) return;
            TruceUntilDay[rivalId] = AbsoluteDay() + days;
        }

        /// <summary>Taking someone off a crew you are paying for peace ends the arrangement.</summary>
        public void BreakTruce(string rivalId)
        {
            if (!string.IsNullOrEmpty(rivalId)) TruceUntilDay.Remove(rivalId);
        }

        // --- territory ----------------------------------------------------

        public string OwnerOf(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            string owner;
            return ZoneOwners.TryGetValue(zoneId, out owner) ? owner : null;
        }

        public bool PlayerOwns(string zoneId) => OwnerOf(zoneId) == Ownership.Player;

        public bool IsContested(string zoneId)
        {
            string owner = OwnerOf(zoneId);
            return !Ownership.IsNeutral(owner) && owner != Ownership.Player;
        }

        public void SetOwner(string zoneId, string ownerId)
        {
            if (string.IsNullOrEmpty(zoneId)) return;
            ZoneOwners[zoneId] = ownerId ?? string.Empty;
        }

        public RivalCrew GetRival(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Rivals.FirstOrDefault(r => r.Id == id);
        }

        /// <summary>Display name for whoever holds a zone.</summary>
        public string OwnerName(string zoneId)
        {
            string owner = OwnerOf(zoneId);
            if (Ownership.IsNeutral(owner)) return "Neutral";
            if (owner == Ownership.Player) return "Yours";
            return GetRival(owner)?.Name ?? owner;
        }

        /// <summary>Pulls every worker off a zone — used when a corner is lost.</summary>
        public void ClearZone(string zoneId)
        {
            foreach (var worker in WorkersIn(zoneId).ToList())
            {
                worker.ZoneId = null;
                worker.State = WorkerState.OffDuty;
            }
        }

        public float GetHeat(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return 0f;
            float v;
            return ZoneHeat.TryGetValue(zoneId, out v) ? v : 0f;
        }

        public void AddHeat(string zoneId, float delta)
        {
            if (string.IsNullOrEmpty(zoneId)) return;
            float v = GetHeat(zoneId) + delta;
            ZoneHeat[zoneId] = v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        // --- houses --------------------------------------------------------

        public bool OwnsHouse(string houseId) =>
            !string.IsNullOrEmpty(houseId) && OwnedHouses.Contains(houseId);

        public IEnumerable<WorkerData> WorkersInHouse(string houseId)
        {
            return Roster.Where(w => w.HouseId == houseId);
        }

        /// <summary>Rooms still free, or 0 if the house is not owned.</summary>
        public int RoomsFree(HouseDef house)
        {
            if (house == null || !OwnsHouse(house.Id)) return 0;
            int used = WorkersInHouse(house.Id).Count();
            int free = house.Rooms - used;
            return free < 0 ? 0 : free;
        }

        public float GetHouseHeat(string houseId)
        {
            if (string.IsNullOrEmpty(houseId)) return 0f;
            float v;
            return HouseHeat.TryGetValue(houseId, out v) ? v : 0f;
        }

        public void AddHouseHeat(string houseId, float delta)
        {
            if (string.IsNullOrEmpty(houseId)) return;
            float v = GetHouseHeat(houseId) + delta;
            HouseHeat[houseId] = v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        public bool IsHouseLocked(string houseId)
        {
            if (string.IsNullOrEmpty(houseId)) return false;
            int until;
            if (!HouseLockedUntilDay.TryGetValue(houseId, out until)) return false;
            return AbsoluteDay() < until;
        }

        public int HouseLockDaysLeft(string houseId)
        {
            int until;
            if (string.IsNullOrEmpty(houseId) ||
                !HouseLockedUntilDay.TryGetValue(houseId, out until)) return 0;
            int left = until - AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        /// <summary>Empties a house — used when it is raided.</summary>
        public void ClearHouse(string houseId)
        {
            foreach (var worker in WorkersInHouse(houseId).ToList())
            {
                worker.HouseId = null;
                worker.State = WorkerState.OffDuty;
            }
        }

        /// <summary>Total rent due at midnight across every house owned.</summary>
        [IgnoreDataMember]
        public int DailyRentBill =>
            Houses.All.Where(h => OwnsHouse(h.Id)).Sum(h => h.DailyRent);

        public WorkerData GetWorker(int id) => Roster.FirstOrDefault(w => w.Id == id);

        public IEnumerable<WorkerData> WorkersIn(string zoneId)
        {
            return Roster.Where(w => w.ZoneId == zoneId);
        }

        /// <summary>
        /// Stable slot index for a worker within its zone, used to keep post
        /// positions consistent between streams.
        /// </summary>
        public int SlotIndexOf(WorkerData worker)
        {
            var zone = Zones.Get(worker.ZoneId);
            if (zone == null) return 0;

            var occupants = WorkersIn(worker.ZoneId).OrderBy(w => w.Id).ToList();
            int idx = occupants.FindIndex(w => w.Id == worker.Id);
            return idx < 0 ? 0 : idx % zone.Slots;
        }

        public WorkerData AddWorker(string name, int modelHash, int tier)
        {
            var worker = new WorkerData
            {
                Id = NextWorkerId++,
                Name = name,
                ModelHash = modelHash,
                Tier = tier
            };

            // Every recruitment path funnels through here, so this is the one
            // place a referral can be spent without each caller having to know
            // referrals exist.
            if (PendingReferrals > 0)
            {
                PendingReferrals--;
                worker.Loyalty += Config.Current.ReferralLoyaltyBonus;

                Notify.Show(
                    $"~g~{name} came recommended.~s~ She has heard how you treat people.");
            }

            worker.Clamp();
            Roster.Add(worker);
            return worker;
        }
    }
}
