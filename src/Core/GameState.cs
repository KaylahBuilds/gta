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
        public const int CurrentSaveVersion = 3;

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
            worker.Clamp();
            Roster.Add(worker);
            return worker;
        }
    }
}
