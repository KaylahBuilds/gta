using System.Collections.Generic;
using GTA;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// Blips nearby prospects while the player is standing on gang turf or around
    /// Vinewood.
    ///
    /// This is what actually makes recruiting *easy* in those areas — the mod
    /// cannot make the game spawn more people, so instead it points at the ones
    /// already there. Ordinary areas get nothing, which is what keeps a hotspot
    /// feeling like one.
    ///
    /// Blips are attached to peds the mod does not own, so they must be deleted
    /// on every refresh and on abort. A leaked blip on an ambient ped outlives
    /// the ped and leaves a marker stuck on the map.
    /// </summary>
    public class ProspectSpotter
    {
        private readonly SpawnManager _spawner;
        private readonly List<Blip> _blips = new List<Blip>();
        private int _nextScan;

        public ProspectSpotter(SpawnManager spawner)
        {
            _spawner = spawner;
        }

        public RecruitArea CurrentArea { get; private set; } = RecruitArea.Ordinary;

        public void Update()
        {
            if (Game.GameTime < _nextScan) return;
            _nextScan = Game.GameTime + Config.Current.ProspectBlipRefreshMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            CurrentArea = RecruitAreas.At(player.Position);

            Clear();

            if (!Config.Current.ShowProspectBlips) return;
            if (!RecruitAreas.IsHotspot(CurrentArea)) return;
            if (GameState.Current.RosterFull) return;

            foreach (var ped in Recruiter.FindProspects(_spawner, Config.Current.MaxProspectBlips))
            {
                if (ped == null || !ped.Exists()) continue;

                Blip blip = ped.AddBlip();
                if (blip == null || !blip.Exists()) continue;

                blip.Sprite = BlipSprite.Friend;
                blip.Color = Config.Current.ProspectBlipColour;
                // Smaller than a crew blip — same colour, so size and label are
                // what tell "already yours" apart from "could be".
                blip.Scale = 0.5f;
                blip.IsShortRange = true;
                blip.Name = "Prospect";

                _blips.Add(blip);
            }
        }

        /// <summary>Safe to call repeatedly and on abort.</summary>
        public void Clear()
        {
            foreach (Blip blip in _blips)
                if (blip != null && blip.Exists()) blip.Delete();

            _blips.Clear();
        }
    }
}
