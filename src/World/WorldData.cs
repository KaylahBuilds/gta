using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OnTheBlade.BladeWorld
{
    /// <summary>
    /// The shape of scripts/BladeWorld/world.json.
    ///
    /// **This file is a wire format and is duplicated verbatim in On The Blade.**
    /// That duplication is deliberate — see the architecture note in PLAN.md §2.
    /// A shared assembly would be tidier and would also mean two independently
    /// updated mods shipping different versions of the same DLL into one SHVDN
    /// domain, a load-order dependency between them, and a missing-assembly crash
    /// for anybody who installs only one. A copied DTO has none of those
    /// failure modes and costs a file.
    ///
    /// Rules for changing it:
    ///
    /// - Only ever add fields. Never rename or repurpose one.
    /// - Bump <see cref="SchemaVersion"/> when you add.
    /// - A reader seeing a higher version than it knows reads what it recognises
    ///   and leaves the rest alone, rather than rewriting the file down a version
    ///   and destroying the other mod's data.
    /// The namespace is BladeWorld rather than World on purpose: a namespace
    /// called World sits alongside GTA's own World class, and every file in the
    /// mod that does `using GTA;` would resolve the shorter name to the namespace
    /// instead — turning World.CreateBlip into a compile error in six files that
    /// have nothing to do with this one.
    ///
    /// - Lists rather than dictionaries: DataContractJsonSerializer renders a
    ///   dictionary as an array of key/value pairs, which is unreadable, and this
    ///   file is meant to be openable by a human.
    ///
    /// - EVERY contract in this file implements IExtensibleDataObject, and that
    ///   is load-bearing rather than tidy. Without it
    ///   DataContractJsonSerializer SILENTLY DISCARDS fields it does not know
    ///   on a round-trip: a mod reads the file, keeps only what its own copy of
    ///   this DTO declares, and writes back a version with everybody else's
    ///   newer fields deleted. No error, no warning, and the loss only shows up
    ///   as another mod's data intermittently reverting. With it, unknown
    ///   fields survive read-modify-write untouched, which is the only thing
    ///   that makes "only ever add fields" actually safe for a THIRD writer
    ///   whose fields these two mods will never have heard of.
    /// </summary>
    [DataContract(Name = "world")]
    public class WorldData : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        public const int CurrentSchema = 4;

        [DataMember(Name = "schemaVersion", Order = 0)]
        public int SchemaVersion = CurrentSchema;

        /// <summary>Which mod wrote it last. Diagnostic only.</summary>
        [DataMember(Name = "updatedBy", Order = 1)]
        public string UpdatedBy;

        [DataMember(Name = "reputation", Order = 2)]
        public Reputation Rep = new Reputation();

        [DataMember(Name = "zones", Order = 3)]
        public List<ZoneEntry> Zones = new List<ZoneEntry>();

        [DataMember(Name = "crews", Order = 4)]
        public List<CrewEntry> Crews = new List<CrewEntry>();

        [DataMember(Name = "police", Order = 5)]
        public PoliceEntry Police = new PoliceEntry();

        [DataMember(Name = "heat", Order = 6)]
        public List<HeatEntry> Heat = new List<HeatEntry>();

        /// <summary>Worker ids The Trap Star is using to carry. Written by trap, read by blade.</summary>
        [DataMember(Name = "couriers", Order = 7)]
        public List<int> Couriers = new List<int>();

        /// <summary>The player's set — name and colours, flown by both mods.
        /// Written by trap, read by blade. Null means no name claimed yet,
        /// and null is what every schema-1 file deserialises to. Schema 2.</summary>
        [DataMember(Name = "set", Order = 8)]
        public SetEntry Set;

        /// <summary>Pillow talk: intel the girls hear from clients, surfaced to
        /// the product side. Written by blade (rolling, pruned by day), read by
        /// trap, which tracks consumed ids locally. Schema 3.</summary>
        [DataMember(Name = "tips", Order = 9)]
        public List<TipEntry> Tips = new List<TipEntry>();

        /// <summary>The wash: dollars per day the girls' venues can launder for
        /// the product side. Written by blade; 0 = no venues. Schema 3.</summary>
        [DataMember(Name = "washCapacity", Order = 10)]
        public int WashCapacityPerDay;


        /// <summary>
        /// THE CITY TICKER — one-line events any pillar may publish and every
        /// pillar's feed may read. Schema 4.
        ///
        /// This is the cheapest possible proof that OneCity is real: a line
        /// about a venue having a night, or a plate going out over the air,
        /// arriving in another mod's feed. Far cheaper than shared mechanics,
        /// and more legible.
        ///
        /// Rules, matching the heat ledger's discipline: every writer APPENDS
        /// its own entries and prunes only entries IT wrote (by Source), so no
        /// mod ever deletes another's news. Rolling and small — the wire is a
        /// ticker, not an archive.
        /// </summary>
        [DataMember(Name = "ticker", Order = 11)]
        public List<TickerEntry> Ticker = new List<TickerEntry>();

        public void EnsureCollections()
        {
            if (Rep == null) Rep = new Reputation();
            if (Zones == null) Zones = new List<ZoneEntry>();
            if (Crews == null) Crews = new List<CrewEntry>();
            if (Police == null) Police = new PoliceEntry();
            if (Heat == null) Heat = new List<HeatEntry>();
            if (Couriers == null) Couriers = new List<int>();
            if (Tips == null) Tips = new List<TipEntry>();
            if (Ticker == null) Ticker = new List<TickerEntry>();
        }

        public HeatEntry HeatFor(string zoneId, bool create)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;

            foreach (var h in Heat)
                if (h.ZoneId == zoneId) return h;

            if (!create) return null;

            var fresh = new HeatEntry { ZoneId = zoneId };
            Heat.Add(fresh);
            return fresh;
        }

        public ZoneEntry ZoneFor(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;

            foreach (var z in Zones)
                if (z.Id == zoneId) return z;

            return null;
        }
    }

    /// <summary>
    /// One street name, earned in two businesses. Two running totals rather than
    /// one value, because a single field would be clobbered by whichever mod
    /// wrote last.
    /// </summary>
    [DataContract(Name = "reputation")]
    public class Reputation : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Name = "blade", Order = 0)] public int Blade;
        [DataMember(Name = "trap", Order = 1)] public int Trap;

        public int Total => Blade + Trap;
    }

    [DataContract(Name = "zone")]
    public class ZoneEntry : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Name = "id", Order = 0)] public string Id;

        /// <summary>"player", a crew id, or empty for neutral. Written by blade.</summary>
        [DataMember(Name = "owner", Order = 1)] public string Owner;

        /// <summary>Absolute day a raid lockout expires. Written by blade.</summary>
        [DataMember(Name = "lockedUntilDay", Order = 2)] public int LockedUntilDay;
    }

    /// <summary>
    /// One set, two businesses. The colour is a raw blip colour id (never 0 —
    /// that is the "unchosen" sentinel on the writer's side) and the tag is the
    /// matching in-game text colour code, precomputed so the reader does not
    /// need the writer's palette.
    /// </summary>
    [DataContract(Name = "set")]
    public class SetEntry : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Name = "name", Order = 0)] public string Name;
        [DataMember(Name = "colourId", Order = 1)] public int ColourId;
        [DataMember(Name = "tag", Order = 2)] public string Tag;
    }

    /// <summary>
    /// One piece of pillow talk. Kind: "raid" (a zone is watched), "stash"
    /// (a bag somebody bragged about, at x/y/z), "whale" (a client wants
    /// weight at a premium). Ids are unique so the reader shows each once.
    /// </summary>
    [DataContract(Name = "tip")]
    public class TipEntry : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Name = "id", Order = 0)] public string Id;
        [DataMember(Name = "kind", Order = 1)] public string Kind;
        [DataMember(Name = "zone", Order = 2)] public string ZoneId;
        [DataMember(Name = "day", Order = 3)] public int Day;
        [DataMember(Name = "x", Order = 4)] public float X;
        [DataMember(Name = "y", Order = 5)] public float Y;
        [DataMember(Name = "z", Order = 6)] public float Z;
        [DataMember(Name = "grams", Order = 7)] public float Grams;
        [DataMember(Name = "perGram", Order = 8)] public int PerGram;
        [DataMember(Name = "product", Order = 9)] public string ProductId;
    }

    [DataContract(Name = "crew")]
    public class CrewEntry : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Name = "id", Order = 0)] public string Id;
        [DataMember(Name = "name", Order = 1)] public string Name;
        [DataMember(Name = "strength", Order = 2)] public float Strength;
        [DataMember(Name = "aggression", Order = 3)] public float Aggression;
        [DataMember(Name = "atWar", Order = 4)] public bool AtWar;
        [DataMember(Name = "truceUntilDay", Order = 5)] public int TruceUntilDay;
    }

    [DataContract(Name = "police")]
    public class PoliceEntry : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        /// <summary>Absolute day the retainer runs out. Written by blade.</summary>
        [DataMember(Name = "retainerUntilDay", Order = 0)] public int RetainerUntilDay;

        /// <summary>Zone the man at the station has warned about, if any.</summary>
        [DataMember(Name = "warnedZone", Order = 1)] public string WarnedZone;

        /// <summary>Somebody in the crew is talking. Written by blade.</summary>
        [DataMember(Name = "informantActive", Order = 2)] public bool InformantActive;
    }

    /// <summary>
    /// Four cells per corner. Each mod writes only its own two and decays only
    /// its own, so nobody ever overwrites a field somebody else owns.
    ///
    /// Effective heat sums the pair for a channel and adds a share of the other
    /// channel — a block under one kind of scrutiny gets more police generally.
    /// </summary>
    [DataContract(Name = "heat")]
    public class HeatEntry : IExtensibleDataObject
    {

        /// <summary>Fields written by a mod that is newer than this one. Held
        /// verbatim and written back out untouched. Never a [DataMember] —
        /// the serialiser owns this slot.</summary>
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Name = "zone", Order = 0)] public string ZoneId;

        [DataMember(Name = "viceBlade", Order = 1)] public float ViceBlade;
        [DataMember(Name = "viceTrap", Order = 2)] public float ViceTrap;
        [DataMember(Name = "narcoBlade", Order = 3)] public float NarcoBlade;
        [DataMember(Name = "narcoTrap", Order = 4)] public float NarcoTrap;

        public float Vice => ViceBlade + ViceTrap;
        public float Narco => NarcoBlade + NarcoTrap;
    }

    /// <summary>
    /// One line of city news. Source is the writing mod ("trap", "blade",
    /// "swipe") — the pruning key, and how a reader skips its own entries.
    /// Day is the writer's absolute day, used only for pruning.
    /// </summary>
    [DataContract(Name = "tick")]
    public class TickerEntry : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }

        [DataMember(Name = "src", Order = 0)] public string Source;
        [DataMember(Name = "text", Order = 1)] public string Text;
        [DataMember(Name = "day", Order = 2)] public int Day;
    }
}
