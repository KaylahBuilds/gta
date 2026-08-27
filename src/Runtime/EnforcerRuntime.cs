using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// The in-world representation of an <see cref="EnforcerData"/>.
    ///
    /// Muscle used to exist only as a number and a wage. The only time one ever
    /// appeared as a ped was mid-turf-battle, spawned by the incident and deleted
    /// when it ended — so a man you paid $350 a day and armed with a carbine was
    /// invisible in the world he was supposedly standing in. You could not look
    /// at him, walk up to him, or tell by looking whether the corner was covered.
    ///
    /// Same contract as <see cref="WorkerRuntime"/>: created when the player comes
    /// near, destroyed when they leave, holding nothing that matters. Everything
    /// meaningful stays on the EnforcerData record.
    /// </summary>
    public class EnforcerRuntime
    {
        public int EnforcerId { get; }
        public Ped Ped { get; private set; }
        public Blip Blip { get; private set; }

        private EnforcerRuntime(int enforcerId, Ped ped, Blip blip)
        {
            EnforcerId = enforcerId;
            Ped = ped;
            Blip = blip;
        }

        public bool IsValid => Ped != null && Ped.Exists() && !Ped.IsDead;

        /// <summary>
        /// Rolling with the player rather than standing a post. A follower is
        /// exempt from the post-distance cull — the stream measures distance to
        /// his POST, and a man following you would evaporate 220 metres from a
        /// corner he is no longer standing on.
        /// </summary>
        public bool Following;

        /// <summary>
        /// Returns null if the model is not resident yet; the caller retries on the
        /// next scan rather than blocking the tick on a load.
        /// </summary>
        public static EnforcerRuntime Create(EnforcerData data, Vector3 position, float heading,
                                             RelationshipGroup crew)
        {
            var model = ModelFor(data);

            // Same one-frame request that made the women invisible.
            if (!SafeGround.RequestPed(model)) return null;

            Ped ped = SafeGround.CreatePed(model, Core.SafeGround.Fix(position), heading);
            model.MarkAsNoLongerNeeded();

            if (ped == null || !ped.Exists()) return null;

            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            ped.CanBeTargetted = true;
            ped.RelationshipGroup = crew;

            // Armed in the world with whatever you actually bought him. The menu
            // has always said he was carrying a carbine; now it is visible, which
            // is most of the reason to spend $22,000 on one.
            var loadout = data.Loadout;
            if (loadout != null && loadout.Weapon != WeaponHash.Unarmed)
                ped.Weapons.Give(loadout.Weapon, loadout.Ammo, false, true);

            ped.Armor = loadout != null ? loadout.Armour : 0;
            ped.Accuracy = loadout != null ? loadout.Accuracy : 20;

            // A man who is laid up is not standing on a corner looking well.
            ped.Task.StartScenarioInPlace(
                data.IsInjured() ? "WORLD_HUMAN_SMOKING" : "WORLD_HUMAN_GUARD_STAND", 0, true);

            Blip blip = ped.AddBlip();
            blip.Sprite = BlipSprite.Devin;
            blip.Color = data.IsInjured() ? BlipColor.Red : BlipColor.Blue;
            blip.Scale = 0.7f;
            blip.IsShortRange = true;
            blip.Name = $"{data.Name} ({Regions.Get(data.RegionId)?.Display ?? data.RegionId})";

            if (Config.Current.LogSpawnDiagnostics)
            {
                Persistence.SaveManager.Log(
                    $"SPAWN-MUSCLE {data.Name} region={data.RegionId} " +
                    $"at=({position.X:0.0},{position.Y:0.0},{position.Z:0.0})");
            }

            return new EnforcerRuntime(data.Id, ped, blip);
        }

        /// <summary>
        /// What he looks like. Drawn from his background rather than randomised,
        /// so an ex-copper and a leg-breaker are not the same man in a different
        /// hat — the background is the most expensive decision when hiring and it
        /// should be visible on the street.
        /// </summary>
        private static Model ModelFor(EnforcerData data)
        {
            // He was given one when he was hired; that is who he is.
            if (!string.IsNullOrEmpty(data.ModelName)) return new Model(data.ModelName);

            // Only reached by a save written before muscle had models. Falls back
            // to his background so an ex-copper and a leg-breaker are still not
            // the same man in a different hat.

            switch ((MuscleBackground)data.Background)
            {
                case MuscleBackground.ExCop:      return new Model("s_m_m_ciasec_01");
                case MuscleBackground.LegBreaker: return new Model("g_m_y_lost_01");
                case MuscleBackground.Fixer:      return new Model("a_m_m_business_01");
                case MuscleBackground.Local:      return new Model("a_m_y_soucent_01");
                case MuscleBackground.Veteran:    return new Model("s_m_m_highsec_01");
                default:                          return new Model("g_m_y_famdnf_01");
            }
        }

        public void Despawn()
        {
            if (Blip != null && Blip.Exists()) Blip.Delete();
            Blip = null;

            if (Ped != null && Ped.Exists())
            {
                Ped.IsPersistent = false;
                Ped.MarkAsNoLongerNeeded();
            }

            Ped = null;
        }
    }
}
