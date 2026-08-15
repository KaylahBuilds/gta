using System;
using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    /// <summary>What kind of client is asking.</summary>
    public enum ClientKind
    {
        None = 0,

        /// <summary>Money. A lot of it, for one night off the street.</summary>
        Whale = 1,

        /// <summary>Somebody with a job worth more than his wallet.</summary>
        Connected = 2
    }

    /// <summary>What a connected client is good for instead of cash.</summary>
    public enum LeverageKind
    {
        None = 0,

        /// <summary>Vice. Buys the heat off every corner you hold.</summary>
        Heat = 1,

        /// <summary>A fixer. Buys peace with whichever crew hates you most.</summary>
        Truce = 2,

        /// <summary>Somebody with an audience. Buys a name and followers.</summary>
        Name = 3
    }

    public class ConnectedDef
    {
        public LeverageKind Kind;
        public string Title;

        /// <summary>What taking the favour instead of the money does, in plain words.</summary>
        public string Offer;
    }

    /// <summary>
    /// The customer side.
    ///
    /// Until now exactly one client was modelled — the one who turns violent —
    /// which meant the actual product interface was a coin flip between "nothing
    /// happened" and "drive across the map". Everything here sits on the other
    /// side of that: clients who are worth having.
    ///
    /// None of it is an errand. Regulars pay passively, and a booking is a
    /// decision taken from a menu with a fade to black at the end of it — the same
    /// abstraction the rest of the mod holds to.
    /// </summary>
    public static class Clients
    {
        public static readonly List<ConnectedDef> Connected = new List<ConnectedDef>
        {
            new ConnectedDef
            {
                Kind = LeverageKind.Heat, Title = "a vice lieutenant",
                Offer = "He can lose some paperwork. Every corner you hold cools off."
            },
            new ConnectedDef
            {
                Kind = LeverageKind.Truce, Title = "somebody's lawyer",
                Offer = "He makes calls for people you fight with. Buys you peace with " +
                        "whichever crew hates you most."
            },
            new ConnectedDef
            {
                Kind = LeverageKind.Name, Title = "a producer",
                Offer = "He knows people worth knowing. Word gets around, and she comes " +
                        "back with an audience."
            }
        };

        public static ConnectedDef GetConnected(LeverageKind kind)
        {
            return Connected.FirstOrDefault(c => c.Kind == kind);
        }

        // Deliberately first names only, in the same register as the crew names —
        // these are people she knows, not characters with a story.
        private static readonly string[] Names =
        {
            "Whitaker", "Mr. Lang", "Delacroix", "Mr. Osei", "Rourke", "Mr. Vance",
            "Halloran", "Mr. Reyes", "Sutcliffe", "Mr. Amari", "Petrossian", "Mr. Blum"
        };

        public static string RollName(Random rng) => Names[rng.Next(Names.Length)];

        /// <summary>
        /// Who gets asked for.
        ///
        /// Tier is the biggest term because it is what the client is paying for,
        /// but Magnetic pulls the good ones as well as the bad — the trait's blurb
        /// promises exactly that and until now only the bad half was implemented.
        /// </summary>
        public static float Appeal(WorkerData worker)
        {
            float appeal = worker.Tier;

            if ((worker.TraitSet & WorkerTrait.Magnetic) != 0) appeal *= 1.5f;
            if ((worker.TraitSet & WorkerTrait.CameraReady) != 0) appeal *= 1.2f;
            if ((worker.TraitSet & WorkerTrait.Hustler) != 0) appeal *= 1.1f;

            // Nobody is asking for someone who is checked out or exhausted.
            appeal *= worker.Loyalty / 100f;
            appeal *= worker.Stamina / 100f;

            return appeal;
        }

        /// <summary>
        /// How likely a booking is to go wrong, 0..1.
        ///
        /// Bigger money means a client with more to lose and fewer people telling
        /// him no. Muscle covering her ground brings it down, which is the first
        /// thing in the mod that makes an enforcer matter to a worker who is not
        /// standing on a corner.
        /// </summary>
        public static float Risk(WorkerData worker, int payout, EnforcerData cover)
        {
            var cfg = Config.Current;

            float risk = cfg.BookingBaseRisk
                         + payout / (float)cfg.BookingRiskPerDollar;

            if ((worker.TraitSet & WorkerTrait.Magnetic) != 0) risk *= 1.25f;
            if ((worker.TraitSet & WorkerTrait.Connected) != 0) risk *= 0.7f;
            if ((worker.TraitSet & WorkerTrait.Fragile) != 0) risk *= 1.15f;

            if (cover != null && cover.IsArmed && !cover.IsInjured())
                risk *= 1f - (cover.EffectiveSkill / 100f) * cfg.BookingMuscleCover;

            // A man assigned to her personally goes with her. This is the only
            // thing in the mod that meaningfully de-risks the biggest bookings,
            // and it costs a whole region's cover to have.
            if (GameState.Current.GuardFor(worker.Id) != null)
                risk *= cfg.GuardBookingRiskReduction;

            if (risk < 0.02f) risk = 0.02f;
            if (risk > 0.85f) risk = 0.85f;
            return risk;
        }

        /// <summary>Whether the player can see the risk before saying yes.</summary>
        public static bool CanScreen => GameState.Current.HasUpgrade(UpgradeCatalog.Screening);

        public static string RiskLabel(float risk)
        {
            if (!CanScreen) return "~c~unknown";
            if (risk < 0.12f) return "~g~low";
            if (risk < 0.28f) return "~y~worth watching";
            if (risk < 0.45f) return "~o~bad odds";
            return "~r~don't";
        }
    }
}
