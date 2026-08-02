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

        /// <summary>Street work only.</summary>
        [DataMember] public int LifetimeEarnings;

        /// <summary>Subscription deposits only.</summary>
        [DataMember] public int LifetimeSubscriptionEarnings;

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
                   && IsOnShift(hour);
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
            if (Followers < 0f) Followers = 0f;
            if (Followers > Config.Current.MaxFollowers) Followers = Config.Current.MaxFollowers;

            // Saves written before models were stored by hash carry only a name.
            if (ModelHash == 0 && !string.IsNullOrEmpty(ModelName))
                ModelHash = new Model(ModelName).Hash;
        }
    }
}
