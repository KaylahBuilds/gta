using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Resolves the business once per in-game hour (~2 real minutes at the
    /// default timescale).
    ///
    ///   income = baseRate[tier] x zone.demand x (loyalty/100) x (1 - heat)
    ///            x (stamina/100) x nightBonus x ownedZoneBonus
    ///            x saturation x traits x vehicle
    /// </summary>
    public class EconomyTick
    {
        private readonly IncidentRoller _incidents;
        private readonly Runtime.SpawnManager _spawner;
        private readonly Random _rng = new Random();

        /// <param name="spawner">
        /// Needed because a crew taking one of yours has to pull her ped as well
        /// as her record — a despawn missed here leaves her standing on a corner
        /// she no longer works for you.
        /// </param>
        public EconomyTick(IncidentRoller incidents, Runtime.SpawnManager spawner)
        {
            _incidents = incidents;
            _spawner = spawner;
        }

        public void Update()
        {
            int hour = GTA.Chrono.GameClock.Hour;
            if (hour == GameState.Current.LastEconomyHour) return;

            GameState.Current.LastEconomyHour = hour;
            Resolve(hour);
        }

        private void Resolve(int hour)
        {
            var state = GameState.Current;
            var cfg = Config.Current;
            bool night = hour >= 20 || hour < 5;

            int take = 0;
            int indoorTake = 0;
            int contentTake = 0;

            foreach (var worker in state.Roster.ToList())
            {
                var zone = Zones.Get(worker.ZoneId);

                // Nobody works themselves unconscious. An exhausted worker used
                // to keep standing there earning a rounding error — payout
                // scales with stamina — while still drawing full heat and still
                // bleeding loyalty, with no route back up because recovery only
                // happens off-shift. She takes the hour instead. The loyalty
                // drain below still fires, so running someone to this point is
                // still a cost; it just is not a one-way trip any more.
                bool spent = worker.IsExhausted;

                bool onTheStreet = worker.ShouldBeOnStreet(hour) && zone != null && !spent;

                // She is with a client. Not on a corner, not in a room, not
                // building an audience — the whole point of a booking is that it
                // costs you her hours.
                if (worker.IsOnBooking) continue;

                var house = Houses.Get(worker.HouseId);
                bool housed = worker.ShouldBeWorkingIndoors(hour)
                              && !spent
                              && house != null
                              && state.OwnsHouse(house.Id)
                              && !state.IsHouseLocked(house.Id);

                bool isContent = house != null && house.IsContentHouse;
                bool indoors = housed && !isContent;
                bool producing = housed && isContent && !state.IsContentShut(house.Id);

                // Stream two accrues every hour either way — that is what makes
                // the two streams compete for the same worker's time. Working
                // indoors is still working: no followers are built in a room.
                // Producing is its own case, because she is building an audience
                // and spending it in the same hour.
                Subscriptions.AccrueHourly(worker,
                    producing ? SubscriptionMode.Producing
                              : onTheStreet || indoors ? SubscriptionMode.Working
                                                       : SubscriptionMode.Idle);

                if (indoors)
                {
                    indoorTake += ResolveIndoor(worker, house, night);
                    continue;
                }

                if (producing)
                {
                    contentTake += ResolveContent(worker, house);
                    continue;
                }

                if (!onTheStreet)
                {
                    // Somewhere of their own to rest. Owning any stash is enough —
                    // off-shift workers aren't tied to a region.
                    // Back-catalogue money, paid here rather than in a branch of
                    // its own on purpose. This block is a fallthrough, not a
                    // guard: anything inserted above it takes the off-shift
                    // resident's stamina recovery away with it, and both the
                    // content rate and her follower equilibrium scale by stamina,
                    // so she would quietly spiral to nothing over about two game
                    // days. She rests AND earns the six per cent.
                    if (isContent && !state.IsContentShut(house.Id)
                                  && !state.IsHouseLocked(house.Id)
                                  && state.OwnsHouse(house.Id))
                    {
                        float share = ContentGear.CatalogueShare(house.Id);
                        if (share > 0f)
                        {
                            int catalogue = (int)Math.Round(
                                ContentHouses.ProjectedHourly(worker, house) * share);

                            if (catalogue > 0)
                            {
                                contentTake += catalogue;
                                worker.LifetimeEarnings += catalogue;
                            }
                        }
                    }

                    float recovery = cfg.StaminaRecoverPerHour;
                    if (state.StashHouses.Count > 0) recovery *= cfg.StashStaminaBonus;

                    worker.Stamina += recovery;
                    worker.Clamp();
                    continue;
                }

                float payout = cfg.BaseRateFor(worker.Tier)
                               * zone.Demand
                               * ZoneUpgrades.DemandMultiplier(zone.Id)
                               * (state.HasUpgrade(UpgradeCatalog.GoodProduct) ? cfg.GoodProductPayout : 1f)
                               * DemandEvents.MultiplierFor(zone.Id)
                               * (worker.Loyalty / 100f)
                               * (1f - state.GetHeat(zone.Id))
                               * (worker.Stamina / 100f)
                               * Saturation(zone, hour, worker)
                               * Traits.StreetMultiplier(worker.TraitSet)
                               * Crew.PayoutBonus(zone.Id)
                               * Pricing.Payout(zone.Id);

                if (night) payout *= cfg.NightDemandBonus;
                payout *= LateWindow.PayoutMultiplier(hour);
                if (state.PlayerOwns(zone.Id)) payout *= cfg.OwnedZoneBonus;
                if (state.ZoneHasVehicle(zone.Id)) payout *= cfg.VehicleDemandBonus;

                // Regulars pay on top of the corner and are not touched by zone
                // demand or saturation — they are hers, not the street's.
                int earned = (int)Math.Round(payout) + ClientBook.RegularIncome(worker);
                if (earned > 0)
                {
                    take += earned;
                    worker.LifetimeEarnings += earned;
                }

                ClientBook.AccrueRegular(worker);

                // An hour on a corner is an hour of experience. Recorded after the
                // payout so a promotion never changes the number just paid out.
                Progression.RecordStreetHour(worker, Crew.MentorBonus(worker, hour));

                // A car means she is not walking the whole shift, and somewhere to
                // sit down between means she is not standing for all of it.
                float drain = cfg.StaminaDrainPerHour;
                if (state.ZoneHasVehicle(zone.Id)) drain *= cfg.VehicleStaminaRelief;
                drain *= ZoneUpgrades.StaminaMultiplier(zone.Id);
                worker.Stamina -= drain;

                state.AddHeat(zone.Id,
                    zone.HeatGain * Traits.HeatMultiplier(worker.TraitSet)
                                  * Crew.HeatMultiplier(zone.Id)
                                  * Law.HeatMultiplierFor(worker)
                                  * Pricing.Heat(zone.Id)
                                  * LateWindow.HeatMultiplier(hour)
                                  * ZoneUpgrades.HeatMultiplier(zone.Id)
                                  * (state.HasUpgrade(UpgradeCatalog.WireBlock)
                                        ? cfg.WireBlockHeatMultiplier : 1f));

                // Working someone into the ground is what actually costs you the
                // roster — this is the tension the management loop runs on.
                if (worker.IsExhausted)
                    worker.Loyalty -= cfg.LoyaltyDrainWhenExhausted
                                      * Traits.LoyaltyDrainMultiplier(worker.TraitSet);

                worker.Clamp();
            }

            // Once per hour, after the roster loop rather than inside it.
            ApplyEventHeat(state, hour);

            DecayHeat();

            if (take > 0)
            {
                Game.Player.Money += take;
                state.LifetimeTake += take;
                Notify.Show($"~g~+${take}~s~  Street take ({hour:00}:00)");
            }

            if (indoorTake > 0)
            {
                Game.Player.Money += indoorTake;
                state.LifetimeTake += indoorTake;
                state.LifetimeHouseTake += indoorTake;
                Notify.Show($"~g~+${indoorTake}~s~  Indoors ({hour:00}:00)");
            }

            if (contentTake > 0)
            {
                Game.Player.Money += contentTake;
                state.LifetimeTake += contentTake;
                state.LifetimeContentTake += contentTake;
                Notify.Show($"~g~+${contentTake}~s~  On camera ({hour:00}:00)");
            }

            CheckRaids();
            CheckHouseRaids();
            CheckContentCondition();

            // Published every hour, after the raids that reset heat, because both
            // mods READ this file every hour. It used to sit in the midnight
            // block: our own raid gate then read a figure up to 23 hours stale,
            // so an hour of decay, a bribe or the laundromat had no effect on our
            // own raid risk until the next midnight, and the other mod raided
            // roughly eight game hours early off the same stale number.
            // Not while saving is blocked. That session is running a BLANK state
            // after an unreadable save, and publishing it would tell The Trap Star
            // every zone is unowned and there are no crews — which it would then
            // act on, through a file that keeps no backup of its own.
            if (!Persistence.SaveManager.SaveBlocked) BladeWorld.WorldLink.Publish();

            // And SAY something. This mod has published territory, crews and
            // the wash capacity both other pillars spend, and has never once
            // spoken in the city it underwrites. A ticker with one writer is a
            // log; two make it a city.
            BladeWorld.CityDesk.ReadIn();
            SaySomething();

            // Bookings settle before a new one can be offered, so a worker who
            // just came back is immediately eligible again rather than sitting
            // out an extra hour for no reason the player can see.
            ClientBook.ResolveBookings();
            ClientBook.RollOffer();

            // Rolled after the take so a new event affects the next hour, not the
            // one the player was just paid for.
            string news = DemandEvents.Tick(_rng);
            if (news != null) Notify.Show(news, true);

            // Midnight is payday, interest day, and where the weekly deposit is
            // checked. EconomyTick only fires on an hour change, so hour 0 comes
            // round exactly once per game day.
            if (hour == 0)
            {
                PayWages();
                StandingOrders.PayRetainers();
                PayRent();
                PayContentReputation();
                PaySubscriptions();
                ClientBook.DecayRegulars();
                Crew.RollExits();
                Crew.RollLoyaltyReferrals();
                Law.TickRetainer();
                Law.RollInformant();
                Law.ReleaseFromCustody();
                Rivals.TickWars();
                Rivals.TickAlliances();
                Rivals.RollPoachAttempt(_spawner);
                AccrueInterest();

                // Flagged rather than started here: this runs at midnight, and the
                // incident roller is what knows whether the player is somewhere an
                // incident can sensibly begin.
                if (Money.ShouldSendCollectors()) state.CollectorsPending = true;
            }

            AwardMilestone();

            // Problems are rolled after the payout so the numbers the player just
            // saw are the ones the roll was based on.
            _incidents.RollHourly();
        }

        /// <summary>
        /// An hour worked indoors.
        ///
        /// No zone demand, no saturation and no night bonus. Rooms are a hard cap,
        /// so crowding is handled by the building rather than by a falloff curve,
        /// and the flat rate around the clock is what makes a house and a corner
        /// worth different things at different times of day.
        /// </summary>
        private int ResolveIndoor(WorkerData worker, HouseDef house, bool night)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            float payout = cfg.BaseRateFor(worker.Tier)
                           * house.RateMultiplier
                           * (worker.Loyalty / 100f)
                           * (1f - state.GetHouseHeat(house.Id))
                           * (worker.Stamina / 100f)
                           * Traits.StreetMultiplier(worker.TraitSet);

            int earned = (int)Math.Round(payout) + ClientBook.RegularIncome(worker);
            if (earned > 0) worker.LifetimeEarnings += earned;

            ClientBook.AccrueRegular(worker);

            // Indoors is still hours on the job — she is earning the same
            // experience, which is what stops the house being a way to park
            // someone forever without her ever progressing.
            Progression.RecordStreetHour(worker);

            worker.Stamina -= cfg.StaminaDrainPerHour * cfg.HouseStaminaDrain;

            state.AddHouseHeat(house.Id,
                house.HeatGain * Traits.HeatMultiplier(worker.TraitSet));

            if (worker.IsExhausted)
                worker.Loyalty -= cfg.LoyaltyDrainWhenExhausted
                                  * Traits.LoyaltyDrainMultiplier(worker.TraitSet);

            worker.Clamp();
            return earned < 0 ? 0 : earned;
        }

        /// <summary>
        /// The hourly weather of every content house: heat in, heat out, and rot
        /// in the ones nobody lives in.
        ///
        /// Heat accrues per RESIDENT rather than per producing hour. That is the
        /// whole reason the shift selector cannot be used to duck it — which is
        /// exactly what went wrong in the brothel ladder, where a fourteen-hour
        /// day shift cut the gain 42% against a flat hourly decay and made every
        /// house permanently heat-immune at full capacity. Here the only lever is
        /// how many people you put in the building, and that lever costs you
        /// precisely the income of the rooms you leave empty.
        ///
        /// Deliberately not given the laundromat bonus: that multiplier alone
        /// would raise the quiet line above capacity and delete the mechanic.
        /// </summary>
        private static void TickContentHouses()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var house in Houses.Content)
            {
                if (!state.OwnsHouse(house.Id)) continue;

                int residents = 0;
                float gain = 0f;

                foreach (var worker in state.WorkersInHouse(house.Id))
                {
                    residents++;
                    gain += house.HeatGain
                            * ContentHouses.HeatReduction(house.Id, worker.TraitSet);
                }

                state.AddHouseHeat(house.Id, gain - cfg.ContentHeatDecayPerHour);

                // A place nobody lives in still goes off, and the rent does not
                // stop. Applied only when empty so it never double-counts with the
                // per-producing-hour wear, and suppressed by a proper set because
                // the point of building it once properly is that it stays built.
                if (residents == 0 && !ContentGear.StopsRot(house.Id))
                    state.AddContentWear(house.Id, cfg.ContentIdleWearPerDay / 24f);
            }
        }

        /// <summary>
        /// An hour in front of a camera.
        ///
        /// Modelled position-for-position on <see cref="ResolveIndoor"/>, with
        /// two deliberate absences. There is no <c>ClientBook.RegularIncome</c>
        /// and no <c>AccrueRegular</c>: a regular is a client who comes to see
        /// her, and she is not seeing anyone. She KEEPS the regulars she has —
        /// DecayRegulars tests IsIndoors, which is true for a resident — she
        /// simply does not work them. On a full book that is real money given up,
        /// which is what stops anybody moving their best earner in here.
        /// </summary>
        private int ResolveContent(WorkerData worker, HouseDef house)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            // Re-checked here and not only at the menu: a raid, a sale or a save
            // edited by hand can all leave more people pointing at a house than it
            // has rooms, and the one who tips it over should earn nothing rather
            // than the house quietly running over capacity forever.
            if (state.ContentResidents(house.Id) > house.Rooms) return 0;

            int earned = ContentHouses.ProjectedHourly(worker, house);
            if (earned > 0) worker.LifetimeEarnings += earned;

            // Hours on camera are still hours on the job. Withholding them would
            // make a permanently parked, never-promoting, never-burning-out worker
            // the optimal play and delete the exit arc for anybody in here.
            Progression.RecordStreetHour(worker);

            worker.Stamina -= cfg.StaminaDrainPerHour * cfg.HouseStaminaDrain;

            // Wear is charged per PRODUCING worker-hour, so an unstaffed house
            // does not wear at all and a bigger house degrades proportionally
            // faster. That last property is the entire reason a proper set is
            // worth buying at the top of the ladder and a donation at the bottom.
            state.AddContentWear(house.Id,
                cfg.ContentWearPerWorkerHour * ContentGear.WearMultiplier(house.Id));

            if (worker.IsExhausted)
                worker.Loyalty -= cfg.LoyaltyDrainWhenExhausted
                                  * Traits.LoyaltyDrainMultiplier(worker.TraitSet);

            worker.Clamp();
            return earned < 0 ? 0 : earned;
        }

        /// <summary>
        /// Turns everyone out of a house that has been left to rot.
        ///
        /// Deliberately unlike a raid. A raid costs you time you cannot buy back
        /// — five days locked and a fine. Neglect costs money you can pay this
        /// second. That is why the content house is the thing that keeps earning
        /// during a hot spell: heat cannot be paid off, wear can.
        ///
        /// Announced once. IsContentShut is a pure function of stored wear and
        /// nothing lowers it but a repair, so without the latch this fired a
        /// blocking notification every game hour — about thirty an hour of real
        /// time — until the player noticed.
        /// </summary>
        private void CheckContentCondition()
        {
            var state = GameState.Current;

            foreach (var house in Houses.Content)
            {
                if (!state.OwnsHouse(house.Id)) continue;

                if (!state.IsContentShut(house.Id))
                {
                    state.ContentShutAnnounced.Remove(house.Id);
                    continue;
                }

                // The latch gates the NOTICE, not the ejection. Gating the
                // ejection meant anybody posted in after the house shut stayed
                // in it forever, earning nothing and still drawing heat, because
                // the transition it was waiting for had already happened.
                bool announce = !state.ContentShutAnnounced.Contains(house.Id);
                if (announce) state.ContentShutAnnounced.Add(house.Id);

                int inside = state.ContentResidents(house.Id);
                state.ClearHouse(house.Id);

                if (!announce) continue;

                Notify.Show(
                    $"~r~{house.Display} is not fit to work in.~s~ " +
                    (inside > 0 ? $"All {inside} of them are out. " : string.Empty) +
                    $"Putting it right costs ${ContentHouses.RepairCost(house):N0}, " +
                    "and the rent does not stop while it sits empty.", true);

                Persistence.SaveManager.Log($"Content house {house.Id} shut for condition.");
            }
        }

        /// <summary>
        /// The only reputation a content-only player can earn.
        ///
        /// Every other Reputation.Award site in the tree is an incident, a
        /// booking, collectors, an accusation, a war or a standing order, and a
        /// woman who never leaves the house reaches none of them — bookings are
        /// explicitly filtered out to make "they only produce content" literal.
        /// Without this the 200/450/700 gates on this very ladder would be
        /// unreachable by the business that is supposed to unlock them.
        /// </summary>
        private static void PayContentReputation()
        {
            var cfg = Config.Current;
            if (!cfg.ReputationEnabled) return;

            var state = GameState.Current;
            float earned = 0f;

            foreach (var house in Houses.Content)
            {
                if (!state.OwnsHouse(house.Id)) continue;
                if (state.IsContentShut(house.Id)) continue;

                int residents = state.ContentResidents(house.Id);
                if (residents <= 0) continue;

                // Floored at 1 per occupied house. The first version was integer
                // residents/4, which paid literally nothing at the entry rung's
                // own recommended occupancy — a mechanic that existed to unblock
                // a gate and awarded zero.
                float share = residents * cfg.ContentReputationPerResident;
                earned += share < 1f ? 1f : share;
            }

            int award = (int)System.Math.Round(earned);
            if (award <= 0) return;

            if (award > cfg.ContentReputationDailyCap) award = cfg.ContentReputationDailyCap;

            Reputation.Award(award);
        }

        /// <summary>
        /// Rent is due whether the rooms were used or not. That is what makes a
        /// house something you have to keep busy rather than something you simply
        /// own, and it is the fastest route into debt in the whole mod.
        /// </summary>
        private void PayRent()
        {
            int bill = GameState.Current.DailyRentBill;
            if (bill <= 0) return;

            Charge(bill, "Rent");
        }

        /// <summary>
        /// The house version of a raid. Rarer than a corner's — heat builds an
        /// order of magnitude slower indoors — but it takes the whole operation in
        /// one night rather than costing a few days on one street.
        /// </summary>
        private void CheckHouseRaids()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var house in Houses.All)
            {
                if (!state.OwnsHouse(house.Id)) continue;
                if (state.GetHouseHeat(house.Id) < cfg.HouseRaidHeatThreshold) continue;
                if (state.IsHouseLocked(house.Id)) continue;

                int inside = state.WorkersInHouse(house.Id).Count();
                if (inside == 0) continue;

                foreach (var worker in state.WorkersInHouse(house.Id).ToList())
                {
                    worker.Loyalty -= cfg.HouseRaidLoyaltyHit;
                    worker.Clamp();
                }

                state.ClearHouse(house.Id);
                state.HouseLockedUntilDay[house.Id] =
                    GameState.AbsoluteDay() + cfg.HouseRaidLockoutDays;
                state.HouseHeat[house.Id] = cfg.HouseRaidHeatAfter;

                Charge(cfg.HouseRaidFine, $"Raid on {house.Display}");

                Notify.Show(
                    $"~r~They came through the door at {house.Display}.~s~ " +
                    $"All {inside} of them out, and the place is shut for " +
                    $"{cfg.HouseRaidLockoutDays} days. The rent does not stop.", true);

                Persistence.SaveManager.Log(
                    $"HOUSE RAID: {house.Id}, {inside} workers removed.");
            }
        }

        /// <summary>
        /// Diminishing returns per corner. Without this, stacking the whole roster
        /// on the highest-demand zone is strictly optimal and territory stops
        /// mattering. Only workers actually out this hour crowd each other.
        /// </summary>
        private float Saturation(ZoneDef zone, int hour, WorkerData self)
        {
            int others = GameState.Current.WorkersIn(zone.Id)
                .Count(w => w.Id != self.Id && w.ShouldBeOnStreet(hour));

            if (others <= 0) return 1f;

            // Pricing bends the crowding curve rather than the yield: a premium
            // corner has fewer clients to go round, so people get in each other's
            // way faster, and a cut-price one absorbs a crowd.
            float falloff = Config.Current.ZoneSaturationFalloff * Pricing.Saturation(zone.Id);
            return 1f / (1f + others * falloff);
        }

        /// <summary>
        /// A demand event's heat is a property of the operation running in that
        /// zone, not of how many people you happen to have standing in it. This
        /// used to be a term inside the per-worker AddHeat call, which silently
        /// multiplied it by headcount — a police operation declaring +0.05/hr
        /// put +0.15/hr on a three-worker corner. Applied once per hour now, and
        /// still only where you actually have somebody posted, because heat you
        /// cannot be caught in is not heat.
        /// </summary>
        private static void ApplyEventHeat(GameState state, int hour)
        {
            float heat = DemandEvents.HeatFor(state.EventZoneId);
            if (heat <= 0f) return;

            bool posted = state.Roster.Any(
                w => w.ZoneId == state.EventZoneId && w.ShouldBeOnStreet(hour));

            if (posted) state.AddHeat(state.EventZoneId, heat);
        }

        private void DecayHeat()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            // Heat bleeds off everywhere, including zones you have pulled out of.
            foreach (var zone in Zones.All)
            {
                float decay = cfg.HeatDecayPerHour;
                if (state.ZoneHasStash(zone.Id)) decay *= cfg.StashHeatDecayBonus;
                if (state.HasUpgrade(UpgradeCatalog.Laundromat)) decay *= cfg.LaunderedHeatDecayBonus;

                // Somebody who used to be on the job knows how a case falls apart.
                // Uses whoever covers the region rather than EnforcerFor, because
                // this is him making calls and not him standing on a corner.
                var local = state.EnforcerInRegion(Regions.ForZone(zone.Id)?.Id);
                if (local != null && !local.IsInjured())
                    decay *= local.Profile.HeatDecayMultiplier;

                state.AddHeat(zone.Id, -decay);
            }

            // Houses cool far more slowly than a corner does. Nothing about an
            // address airs out the way a street does when you pull people off it.
            foreach (var house in Houses.All)
            {
                if (!state.OwnsHouse(house.Id)) continue;

                // Content houses run their own heat entirely and must not take
                // this decay as well. Left unbranched they would cool at 0.021/hr
                // against a full-capacity gain of 0.006 — a quiet line of 245% of
                // capacity at the entry rung and 333% with the laundromat, which
                // makes the raid unreachable and leaves both the heat bill and the
                // discretion upgrade with nothing to act on.
                if (house.IsContentHouse) continue;

                float decay = cfg.HouseHeatDecayPerHour;
                if (state.HasUpgrade(UpgradeCatalog.Laundromat)) decay *= cfg.LaunderedHeatDecayBonus;

                state.AddHouseHeat(house.Id, -decay);
            }

            TickContentHouses();
        }

        /// <summary>
        /// Heat used to be a soft tax with no ceiling. Past the threshold the zone
        /// gets turned over: everyone off the street, the corner locked for a few
        /// days, and a fine. This is what makes laundering and stash houses read
        /// as insurance rather than nice-to-haves.
        /// </summary>
        private void CheckRaids()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            Law.ClearWarningIfSafe();

            foreach (var zone in Zones.All)
            {
                // The shared figure, not ours alone: a block the other business
                // has been cooking is a block vice are already looking at.
                if (BladeWorld.WorldLink.ViceHeat(zone.Id) < cfg.RaidHeatThreshold) continue;
                if (state.IsZoneLocked(zone.Id)) continue;
                if (!state.WorkersIn(zone.Id).Any()) continue;

                // A man on the payroll turns the first raid into a phone call.
                // If the corner is still hot and still staffed when the notice
                // runs out, it goes ahead anyway — he warns, he does not cancel.
                if (Law.WarnInsteadOfRaid(zone)) continue;

                state.ClearZone(zone.Id);
                state.ZoneLockedUntilDay[zone.Id] = GameState.AbsoluteDay() + cfg.RaidLockoutDays;
                state.ZoneHeat[zone.Id] = cfg.RaidHeatAfter;

                Charge(cfg.RaidFine, $"Raid on {zone.Display}");

                Notify.Show(
                    $"~r~{zone.Display} got turned over.~s~ Everyone is off the street and " +
                    $"the corner is shut for {cfg.RaidLockoutDays} days.", true);
            }
        }

        private void AwardMilestone()
        {
            var milestone = MilestoneCatalog.CheckNext(GameState.Current);
            if (milestone == null) return;

            if (milestone.Reward > 0)
            {
                Game.Player.Money += milestone.Reward;
                Notify.Show(
                    $"~g~{milestone.Name}~s~ — {milestone.Blurb} ~g~+${milestone.Reward:N0}", true);
            }
            else
            {
                Notify.Show($"~g~{milestone.Name}~s~ — {milestone.Blurb}", true);
            }
        }

        /// <summary>
        /// The weekly deposit. Unlike the street take this cannot fail, generates
        /// no heat and cannot trigger an incident — it is the safe stream, and it
        /// is paid for in worker-hours not spent earning on the street.
        /// </summary>
        private void PaySubscriptions()
        {
            int deposited = Subscriptions.TryPayout();
            if (deposited <= 0) return;

            var state = GameState.Current;
            Game.Player.Money += deposited;
            state.LifetimeTake += deposited;
            state.LifetimeSubscriptionTake += deposited;

            Notify.Show($"~g~+${deposited:N0}~s~  {Subscriptions.Brand} deposits cleared.", true);

            if (!state.HasUpgrade(UpgradeCatalog.Laundromat)) return;

            // Washing the money is what keeps it from leading anywhere. Only the
            // corners you hold benefit — you cannot launder heat off someone
            // else's ground.
            var cfg = Config.Current;
            int washed = (int)Math.Round(deposited * cfg.LaundromatWashShare);

            foreach (var zone in Zones.All.Where(z => state.PlayerOwns(z.Id)))
                state.AddHeat(zone.Id, -cfg.LaundromatHeatWash);

            Notify.Show(
                $"~g~${washed:N0} washed.~s~ Your corners cooled off " +
                $"{cfg.LaundromatHeatWash * 100:0} points.");
        }

        /// <summary>
        /// Enforcers who don't get paid used to simply walk. Now the shortfall
        /// becomes debt — that is the only reason the operation can ever fail.
        /// </summary>
        private void PayWages()
        {
            var state = GameState.Current;
            if (state.Enforcers.Count == 0) return;

            int bill = state.DailyWageBill;
            Charge(bill, $"Wages for {state.Enforcers.Count}");
        }

        private void AccrueInterest()
        {
            var state = GameState.Current;
            var cfg = Config.Current;
            if (state.Debt <= 0) return;

            int interest = (int)Math.Round(state.Debt * cfg.DebtInterestPerDay);
            state.Debt += interest;

            Notify.Show($"~r~Debt is now ${state.Debt:N0}~s~ (+${interest:N0} interest).", true);

            if (state.Debt >= cfg.DebtCollapseThreshold) Collapse();
        }

        /// <summary>
        /// The lose condition. Not a game over — the save survives, because
        /// deleting somebody's progress outright is a worse outcome than making
        /// them start the street operation again.
        ///
        /// Property used to be kept, and that turned the lose condition into an
        /// unwinnable one. Rent is charged unconditionally and nothing in the mod
        /// sells a house, so a collapsed player woke up with an empty roster and
        /// both ladders still billing — far more than a fresh roster at the
        /// surviving cap can earn — and collapsed again about every five days,
        /// forever. The leases go with everything else, and the menu now has a
        /// way to give one up before it comes to this.
        /// </summary>
        private void Collapse()
        {
            var state = GameState.Current;

            int lost = state.Roster.Count;
            state.Roster.Clear();

            foreach (var zone in Zones.All)
                if (state.PlayerOwns(zone.Id)) state.SetOwner(zone.Id, string.Empty);

            state.Enforcers.Clear();

            int leases = state.OwnedHouses.Count;
            state.OwnedHouses.Clear();
            state.ContentWear.Clear();
            state.ContentGear.Clear();
            state.ContentShutAnnounced.Clear();
            state.HouseHeat.Clear();
            state.HouseLockedUntilDay.Clear();
            state.StandingOrders.Clear();
            state.CrewArmour.Clear();
            state.CrewVehicles.Clear();

            state.Debt = 0;
            state.TimesCollapsed++;

            Notify.Show(
                "~r~The people you owe came collecting.~s~ " +
                $"{lost} gone, every corner given up" +
                (leases > 0 ? $", and {leases} lease(s) with them" : string.Empty) +
                ". You start again with nothing owed.", true);

            Persistence.SaveManager.Log($"COLLAPSE #{state.TimesCollapsed}: lost {lost} workers.");
        }

        /// <summary>
        /// Takes money if there is any, and turns whatever is left into debt.
        /// Every outgoing cost in the mod routes through here.
        /// </summary>
        public static void Charge(int amount, string reason)
        {
            if (amount <= 0) return;
            var state = GameState.Current;

            int paid = Math.Min(Game.Player.Money, amount);
            Game.Player.Money -= paid;

            int shortfall = amount - paid;
            if (shortfall <= 0)
            {
                Notify.Show($"~y~-${paid:N0}~s~  {reason}.");
                return;
            }

            state.Debt += shortfall;
            Notify.Show(
                $"~r~{reason}: ${shortfall:N0} you don't have.~s~ Debt is now ${state.Debt:N0}.", true);
        }

        /// <summary>
        /// One line about the night this business actually had, at most once a
        /// day, and only when there is something to say.
        ///
        /// Never invented mood — it reads the state and reports it, the same
        /// rule the other pillar's feed follows, because a ticker that makes
        /// things up is worse than a silent one.
        /// </summary>
        private static int _lastSaidDay;

        private static void SaySomething()
        {
            var state = GameState.Current;
            int today = GameState.AbsoluteDay();

            if (today == _lastSaidDay) return;
            _lastSaidDay = today;

            int venues = Core.Houses.All.Count(h => state.OwnsHouse(h.Id));
            if (venues <= 0) return;

            // Same figure WorldLink publishes, computed the same way rather
            // than re-derived — two places disagreeing about the wash is
            // exactly the kind of drift that is invisible until it matters.
            int wash = state.Roster.Count > 0 ? 8000 + state.Roster.Count * 2000 : 0;

            if (wash > 12000)
            {
                BladeWorld.CityDesk.Publish(
                    "Somebody's places are turning over serious paper and none " +
                    "of it looks like it came off a street.");
                return;
            }

            if (venues >= 2)
            {
                BladeWorld.CityDesk.Publish(
                    "Two of the late venues had a night. Cars down the block " +
                    "and nobody counting who went in.");
                return;
            }

            BladeWorld.CityDesk.Publish(
                "Quiet week on the late side of town. Which is its own kind of " +
                "news round here.");
        }

    }
}
