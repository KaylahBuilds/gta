using System.Runtime.Serialization;
using GTA;

namespace OnTheBlade.Core
{
    public enum WorkerState
    {
        OffDuty = 0,
        Working = 1,
        InTrouble = 2
    }

    /// <summary>
    /// When an assigned worker actually stands on her corner.
    ///
    /// <c>Always</c> is deliberately 0 so that saves written before shifts
    /// existed deserialise to the old behaviour rather than silently benching
    /// the whole roster.
    /// </summary>
    public enum WorkerShift
    {
        Always = 0,
        Days = 1,
        Nights = 2,
        Off = 3
    }

    /// <summary>
    /// The authoritative record for a crew member. This is the source of truth
    /// and always lives in memory — the in-world <see cref="Runtime.WorkerRuntime"/>
    /// is a disposable view of it that only exists near the player.
    /// </summary>
    [DataContract]
    public class WorkerData
    {
        [DataMember] public int Id;
        [DataMember] public string Name;
        /// <summary>
        /// Optional label. Prospects are now recruited from arbitrary world peds,
        /// so there is usually no name available — <see cref="ModelHash"/> is the
        /// authoritative field.
        /// </summary>
        [DataMember] public string ModelName;

        /// <summary>Ped model to re-create this worker with.</summary>
        [DataMember] public int ModelHash;

        /// <summary>1-3. Gates which zones this worker may be posted to.</summary>
        [DataMember] public int Tier = 1;

        /// <summary>0-100. Scales payout; a walk-off is rolled below 25.</summary>
        [DataMember] public float Loyalty = 60f;

        /// <summary>0-100. Drains on shift, recovers off duty.</summary>
        [DataMember] public float Stamina = 100f;

        /// <summary>Null or empty means off duty.</summary>
        [DataMember] public string ZoneId;

        /// <summary>
        /// Indoor posting. Mutually exclusive with <see cref="ZoneId"/> — she is on
        /// a corner or she is in a house, never both.
        ///
        /// Indoor workers are never spawned as peds, so nothing in the streaming
        /// layer has to know houses exist.
        /// </summary>
        [DataMember] public string HouseId;

        [IgnoreDataMember]
        public bool IsIndoors => !string.IsNullOrEmpty(HouseId);

        /// <summary>Rival crew she was taken from, if any. Null means unattached.</summary>
        [DataMember] public string ClaimedFrom;

        [IgnoreDataMember]
        public bool WasPoached => !string.IsNullOrEmpty(ClaimedFrom);

        [DataMember] public WorkerState State = WorkerState.OffDuty;

        /// <summary>When she works her post. Nights pay 1.6x; days build followers.</summary>
        [DataMember] public WorkerShift Shift = WorkerShift.Always;

        /// <summary>Bitfield of <see cref="WorkerTrait"/>.</summary>
        [DataMember] public int TraitFlags;

        [IgnoreDataMember]
        public WorkerTrait TraitSet
        {
            get { return (WorkerTrait)TraitFlags; }
            set { TraitFlags = (int)value; }
        }

        /// <summary>Subscriber count driving the weekly deposit. Builds while off
        /// the street, decays while on it.</summary>
        [DataMember] public float Followers;

        /// <summary>
        /// Clients who come back for her specifically. Pays passively every hour
        /// she works, and is the only income in the mod that rewards leaving
        /// somebody in one place instead of shuffling the roster to chase demand.
        ///
        /// Built slowly by working and by handling clients well; lost when she is
        /// benched, because a regular who turns up twice to nobody stops turning up.
        /// </summary>
        [DataMember] public int Regulars;

        /// <summary>Absolute hour a booking ends. 0 means she is not on one.</summary>
        [DataMember] public int BookingEndsAtHour;

        /// <summary>What the booking pays if it goes well.</summary>
        [DataMember] public int BookingPayout;

        /// <summary>Rolled once at acceptance so the odds cannot be re-rolled by saving.</summary>
        [DataMember] public float BookingRisk;

        [DataMember] public string BookingClientName;

        /// <summary>Which favour was taken instead of cash. None means she was paid.</summary>
        [DataMember] public int BookingLeverage;

        [IgnoreDataMember]
        public bool IsOnBooking => BookingEndsAtHour > 0;

        /// <summary>Street work only.</summary>
        [DataMember] public int LifetimeEarnings;

        /// <summary>Subscription deposits only.</summary>
        [DataMember] public int LifetimeSubscriptionEarnings;

        /// <summary>
        /// In-game hours actually worked on a corner. This is the experience that
        /// earns a promotion — deliberately not lifetime earnings, which would let
        /// a tier-3 worker promote far faster than a tier-1 for the same shift.
        ///
        /// Off-duty hours do not count. A worker kept permanently on the camera
        /// stream never promotes on her own, which is the reason the wardrobe is
        /// worth paying for.
        /// </summary>
        [DataMember] public int HoursWorked;

        /// <summary>
        /// Hours worked across her whole time with you. Unlike
        /// <see cref="HoursWorked"/> this is never reset by a promotion, because
        /// burnout is about how long she has been doing this and not about how far
        /// she is from the next tier.
        /// </summary>
        [DataMember] public int LifetimeHours;

        /// <summary>
        /// Zone she runs instead of working. Mutually exclusive with a post — a
        /// manager earns nothing herself, which is what makes it a trade rather
        /// than a free bonus.
        /// </summary>
        [DataMember] public string ManagesZoneId;

        [IgnoreDataMember]
        public bool IsManager => !string.IsNullOrEmpty(ManagesZoneId);

        /// <summary>
        /// Absolute day she gets out. Nonzero means she is inside and cannot be
        /// posted, spawned, booked or promoted.
        /// </summary>
        [DataMember] public int JailedUntilDay;

        public bool IsJailed() => JailedUntilDay > 0 && GameState.AbsoluteDay() < JailedUntilDay;

        public int JailDaysLeft()
        {
            int left = JailedUntilDay - GameState.AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        /// <summary>0 none, 1 burnout, 2 her own reasons. See <see cref="Systems.Crew"/>.</summary>
        [DataMember] public int WantsOutReason;

        /// <summary>Absolute day she asked. She goes on her own if left too long.</summary>
        [DataMember] public int WantsOutSinceDay;

        [IgnoreDataMember]
        public bool WantsOut => WantsOutReason != 0;

        /// <summary>
        /// Studio time has been paid for — a permanent follower multiplier for
        /// this worker alone.
        ///
        /// Stored as a flag rather than a float on purpose: a float deserialises
        /// to 0 on a save written before it existed and would silently zero her
        /// follower gain, which is the exact trap the v4 migration had to catch
        /// for the demand-event multiplier.
        /// </summary>
        [DataMember] public bool HasStudio;

        [IgnoreDataMember]
        public bool IsExhausted => Stamina <= 10f;

        /// <summary>Nights run 20:00–05:59, days 06:00–19:59.</summary>
        public bool IsOnShift(int hour)
        {
            switch (Shift)
            {
                case WorkerShift.Always: return true;
                case WorkerShift.Days: return hour >= 6 && hour < 20;
                case WorkerShift.Nights: return hour >= 20 || hour < 6;
                default: return false;
            }
        }

        /// <summary>
        /// The single source of truth for "is she out there right now". Assignment
        /// (<see cref="ZoneId"/>) is persistent; the shift decides whether it is
        /// being worked this hour.
        /// </summary>
        public bool ShouldBeOnStreet(int hour)
        {
            return State == WorkerState.Working
                   && !string.IsNullOrEmpty(ZoneId)
                   && !IsJailed()
                   && IsOnShift(hour);
        }

        /// <summary>
        /// The indoor counterpart. Kept separate from <see cref="ShouldBeOnStreet"/>
        /// on purpose: that method is what the spawner and the incident roller ask,
        /// and both must keep answering "no" for someone who is inside.
        /// </summary>
        public bool ShouldBeWorkingIndoors(int hour)
        {
            return State == WorkerState.Working
                   && IsIndoors
                   && !IsJailed()
                   && IsOnShift(hour);
        }

        /// <summary>Working anywhere this hour, indoors or out.</summary>
        public bool IsWorkingThisHour(int hour)
        {
            return ShouldBeOnStreet(hour) || ShouldBeWorkingIndoors(hour);
        }

        public string ShiftLabel
        {
            get
            {
                switch (Shift)
                {
                    case WorkerShift.Days: return "Days";
                    case WorkerShift.Nights: return "Nights";
                    case WorkerShift.Off: return "Not working";
                    default: return "Always";
                }
            }
        }

        public void Clamp()
        {
            if (Loyalty < 0f) Loyalty = 0f;
            if (Loyalty > 100f) Loyalty = 100f;
            if (Stamina < 0f) Stamina = 0f;
            if (Stamina > 100f) Stamina = 100f;
            if (Tier < 1) Tier = 1;
            if (Tier > 3) Tier = 3;
            if (HoursWorked < 0) HoursWorked = 0;
            if (Regulars < 0) Regulars = 0;
            if (Regulars > Config.Current.MaxRegulars) Regulars = Config.Current.MaxRegulars;
            if (BookingEndsAtHour < 0) BookingEndsAtHour = 0;
            if (BookingPayout < 0) BookingPayout = 0;
            if (Followers < 0f) Followers = 0f;
            if (Followers > Config.Current.MaxFollowers) Followers = Config.Current.MaxFollowers;

            // Saves written before models were stored by hash carry only a name.
            if (ModelHash == 0 && !string.IsNullOrEmpty(ModelName))
                ModelHash = new Model(ModelName).Hash;
        }
    }
}
