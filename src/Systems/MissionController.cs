using GTA;
using OnTheBlade.Core;
using OnTheBlade.Systems.Incidents;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Runs at most one incident at a time.
    ///
    /// The cap is a design decision, not a limitation: two simultaneous blipped
    /// emergencies on opposite sides of the map is not a choice, it is just a
    /// guaranteed failure. A cooldown after each one keeps the operation from
    /// feeling like a fire alarm.
    /// </summary>
    public class MissionController
    {
        private Incident _active;
        private int _cooldownUntil;

        public bool Busy => _active != null && !_active.Finished;

        public bool Ready => !Busy && Game.GameTime >= _cooldownUntil;

        public string ActiveTitle => _active?.Title;

        public void Update()
        {
            if (_active == null) return;

            _active.Update();

            if (_active.Finished)
            {
                _active.Cleanup();
                _active = null;
                _cooldownUntil = Game.GameTime + Config.Current.IncidentCooldownMs;
            }
        }

        public bool TryStart(Incident incident)
        {
            if (!Ready || incident == null) return false;

            _active = incident;
            incident.Start();

            // Start() bails if the worker vanished between the roll and here.
            if (incident.Finished)
            {
                incident.Cleanup();
                _active = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Script reload or game exit. Releases spawned peds and un-sticks any
        /// worker left in the InTrouble state, which would otherwise persist into
        /// the save and never resolve.
        /// </summary>
        public void Abort()
        {
            if (_active == null) return;

            var worker = _active.Worker;
            if (worker != null && worker.State == WorkerState.InTrouble)
            {
                worker.State = string.IsNullOrEmpty(worker.ZoneId)
                    ? WorkerState.OffDuty
                    : WorkerState.Working;
            }

            _active.Cleanup();
            _active = null;
        }
    }
}
