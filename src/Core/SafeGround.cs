using System;
using GTA;
using GTA.Math;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Ground a person can actually be REACHED on — ported from The Trap
    /// Star's placement authority after girls and turf crews kept spawning
    /// inside the Winston House shell where nobody could defend or fight
    /// them. Three rules: never an interior, never a different LAYER than
    /// the street the point came from (a one-story roof or an interior
    /// mezzanine clears naive height checks), and when nothing validates,
    /// the nearest sidewalk beats the raw point every time.
    /// </summary>
    public static class SafeGround
    {
        public static Vector3 Fix(Vector3 wanted)
        {
            // The origin is not trusted. Every test below measures against
            // `wanted`, so an origin carrying a rooftop height LICENSES a
            // rooftop result — the candidate is faithfully "on the same layer"
            // as a bad reference and passes the whole stack. The player is the
            // only anchor that cannot lie about where the ground is.
            var anchor = Game.Player.Character;
            if (anchor != null && anchor.Exists())
            {
                float under = World.GetGroundHeight(
                    new Vector3(wanted.X, wanted.Y, anchor.Position.Z + 1.5f));

                if (under > 0.5f && wanted.Z > under + 4f) wanted.Z = under + 1f;
            }

            Vector3 safe;
            bool got = World.GetSafePositionForPed(wanted, out safe,
                GetSafePositionFlags.NotWater | GetSafePositionFlags.NotInterior);

            if (got && safe != Vector3.Zero
                && SameLayer(safe, wanted)
                && Walkable(wanted, safe))
                return safe;

            // Nothing valid at the point: the pavement by the nearest road is
            // outdoors — but it is CHECKED, not assumed. This path used to
            // return whatever it was handed, so a rejected candidate still
            // reached the spawner by the back door.
            Vector3 walk = World.GetNextPositionOnSidewalk(wanted);
            if (walk != Vector3.Zero && walk.DistanceTo(wanted) <= 40f
                && SameLayer(walk, wanted))
                return walk;

            // Last resort used to be the RAW point — "better a clipped ped
            // than a missing scene" — and a raw point keeps a rooftop's
            // height. Force it down onto the ground underneath instead.
            return ToGroundLayer(wanted);
        }

        /// <summary>Forces a point onto the ground beneath it, whatever height
        /// it arrived carrying.</summary>
        private static Vector3 ToGroundLayer(Vector3 at)
        {
            float ground = World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 1.5f));
            if (ground > 0.5f) { at.Z = ground + 1f; return at; }

            ground = World.GetGroundHeight(new Vector3(at.X, at.Y, at.Z + 60f));
            if (ground > 0.5f) at.Z = ground + 1f;
            return at;
        }

        /// <summary>The pathfinder's own verdict: if walking to the candidate
        /// costs wildly more than the direct distance (ladders, catwalks,
        /// fenced yards), nobody spawns there.</summary>
        private static bool Walkable(Vector3 from, Vector3 to)
        {
            float direct = from.DistanceTo(to);
            float travel = GTA.Native.Function.Call<float>(
                GTA.Native.Hash.CALCULATE_TRAVEL_DISTANCE_BETWEEN_POINTS,
                from.X, from.Y, from.Z, to.X, to.Y, to.Z);

            return travel <= direct * 4f + 25f;
        }

        /// <summary>Probes for ground from just above the ORIGIN's height at
        /// the candidate's X/Y — a point on any roof or interior floor
        /// disagrees with the ground found from street level.</summary>
        private static bool SameLayer(Vector3 candidate, Vector3 origin)
        {
            float drop = Math.Abs(candidate.Z - origin.Z);

            float ground = World.GetGroundHeight(
                new Vector3(candidate.X, candidate.Y, origin.Z + 1.5f));

            if (ground > 0.5f) return drop <= 6f && Math.Abs(ground - candidate.Z) <= 2.5f;

            // No reading. This used to return TRUE — fail open — and "no
            // reading" is exactly what industrial decking and container yards
            // produce. Hold the candidate to a height a person could climb.
            return drop <= 2.5f;
        }

        // --- the registry and the sweep --------------------------------------

        /// <summary>
        /// EVERY ped this mod creates.
        ///
        /// Fix() above stops a bad placement at birth, which is not the same as
        /// keeping a person on the ground: peds walk, get shoved, ride physics,
        /// and get moved by other mods' navmesh. The product side learned this
        /// the expensive way — five rooftop screenshots, each from whichever
        /// spawner had not been wired to the backstop — so here the backstop is
        /// not something a spawner can forget to call. Spawn through this door
        /// and the sweep covers you.
        /// </summary>
        private static readonly System.Collections.Generic.List<Ped> Owned =
            new System.Collections.Generic.List<Ped>();

        private static int _nextSweepAt;
        private const int MaxTracked = 256;

        /// <summary>
        /// Models we have asked the streamer for and not yet handed back.
        ///
        /// This exists because of the bug that made this class's whole cast
        /// invisible. The three spawners that make PEOPLE — workers, muscle and
        /// incident antagonists — asked for their model with a ONE FRAME budget
        /// and then, on failing to get it, immediately revoked the request:
        ///
        ///     model.Request(0);
        ///     if (!model.IsLoaded) { model.MarkAsNoLongerNeeded(); return null; }
        ///
        /// A cold ped model cannot load in one frame. So every scan asked, waited
        /// a frame, cancelled its own request and gave up — and the "retry next
        /// scan" the caller relied on could never accumulate an inch of progress.
        /// It restarted from nothing every 750ms, forever, silently.
        ///
        /// The cure is to leave the request STANDING between attempts, which is
        /// what makes retrying converge. That trades against the rule this
        /// codebase holds everywhere else — every Request is matched by a release
        /// on every path — so the outstanding ones are tracked here and released
        /// together when the stream shuts down. Bounded, and never leaked past
        /// the session.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<int> Pending =
            new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// Ask the streamer for a ped model, and say honestly whether we got it.
        ///
        /// The budget matches the scenery spawners, which were always given one:
        /// HouseTraffic and HouseDoors at 500ms, RivalGirls at 250. The people
        /// this business is actually about were the only ones asking for zero.
        /// </summary>
        public static bool RequestPed(Model model, int timeoutMs = 500)
        {
            if (!model.IsInCdImage || !model.IsPed) return false;

            if (model.Request(timeoutMs))
            {
                Pending.Remove(model.Hash);
                return true;
            }

            // Deliberately NOT released. The next attempt inherits the loading
            // this one paid for; releasing here is precisely what made the retry
            // loop spin forever.
            Pending.Add(model.Hash);
            return false;
        }

        /// <summary>Hands back every model still loading for a spawn that never
        /// happened. Called from the stream's shutdown, so a script reload or a
        /// quit never leaves one flagged.</summary>
        public static void ReleasePendingModels()
        {
            foreach (int hash in Pending) new Model(hash).MarkAsNoLongerNeeded();
            Pending.Clear();
        }

        public static Ped CreatePed(Model model, Vector3 position)
        {
            var ped = World.CreatePed(model, position);
            Watch(ped);
            return ped;
        }

        public static Ped CreatePed(Model model, Vector3 position, float heading)
        {
            var ped = World.CreatePed(model, position, heading);
            Watch(ped);
            return ped;
        }

        public static void Watch(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;

            if (Owned.Count >= MaxTracked) Owned.RemoveAt(0);
            Owned.Add(ped);
        }

        /// <summary>Puts one of ours back on the ground under his own feet.</summary>
        public static bool HealIfOffLayer(Ped ped, Vector3 reference)
        {
            if (ped == null || !ped.Exists() || ped.IsDead) return false;

            // Indoors is not off-layer. An interior floor sits at an arbitrary
            // height above whatever the map holds at those coordinates, so
            // every girl working a walk-up would read as standing on a roof.
            if (GTA.Native.Function.Call<int>(
                    GTA.Native.Hash.GET_INTERIOR_FROM_ENTITY, ped.Handle) != 0)
                return false;

            Vector3 at = ped.Position;
            float ground = World.GetGroundHeight(
                new Vector3(at.X, at.Y, reference.Z + 1.5f));

            if (ground <= 0.5f) return false;      // no reading, no verdict
            if (at.Z <= ground + 4f) return false; // he's fine

            ped.Position = Fix(new Vector3(at.X, at.Y, ground));
            return true;
        }

        /// <summary>One call a tick from the script root.</summary>
        public static void HealAll(Vector3 reference)
        {
            if (Game.GameTime < _nextSweepAt) return;
            _nextSweepAt = Game.GameTime + 900;

            for (int i = Owned.Count - 1; i >= 0; i--)
            {
                var ped = Owned[i];

                if (ped == null || !ped.Exists()) { Owned.RemoveAt(i); continue; }
                if (ped.IsDead || ped.IsInVehicle()) continue;

                HealIfOffLayer(ped, reference);
            }
        }

        /// <summary>Script reload only. Handles, not ownership — never releases.</summary>
        public static void ForgetAll() => Owned.Clear();
    }
}
