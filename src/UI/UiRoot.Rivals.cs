using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;

namespace OnTheBlade.UI
{
    /// <summary>
    /// The crews, and the option to pay them off.
    ///
    /// Rivals used to be visible only as a line of text inside a zone's
    /// description, and the only verb available against them was violence. A
    /// truce turns them into something you can negotiate with, gives money a
    /// third sink alongside property and muscle, and makes a strong crew a
    /// problem you can defer rather than only fight.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _rivals;

        private void BuildRivalsMenu()
        {
            _rivals = new NativeMenu("On The Blade", "THE CREWS");
            _pool.Add(_rivals);
            _main.AddSubMenu(_rivals);
            _rivals.Shown += (s, e) => RebuildRivals();
        }

        /// <summary>
        /// Peace costs more from a strong, angry crew, and less once they respect
        /// you — which is most of what reputation is for.
        /// </summary>
        public static int ProtectionCost(RivalCrew rival)
        {
            var cfg = Config.Current;

            float price = cfg.ProtectionBaseCost
                          * (0.5f + rival.Strength / 100f)
                          * (0.6f + rival.Aggression)
                          * Reputation.ProtectionDiscount(GameState.Current.Reputation);

            return (int)Math.Round(price / 100f) * 100;
        }

        private void RebuildRivals()
        {
            _rivals.Clear();
            var state = GameState.Current;

            foreach (var rival in state.Rivals.OrderByDescending(r => r.Strength))
            {
                int held = Zones.All.Count(z => state.OwnerOf(z.Id) == rival.Id);

                if (rival.IsBroken)
                {
                    _rivals.Add(new NativeItem(rival.Name, "Finished. They aren't coming back.")
                    {
                        AltTitle = "~g~broken",
                        Enabled = false
                    });
                    continue;
                }

                if (rival.IsAllied())
                {
                    _rivals.Add(new NativeItem(rival.Name,
                        $"With you. They stay off your corners and they turn out when you " +
                        $"go and take somebody else's. Strength {rival.Strength:0}, holding {held}.")
                    {
                        AltTitle = $"~b~allied {rival.AllyDaysLeft()}d",
                        Enabled = false
                    });
                    continue;
                }

                var cfg = Config.Current;

                // War: peace is still for sale, at a price meant to hurt.
                if (rival.IsAtWar)
                {
                    int warCost = (int)Math.Round(
                        ProtectionCost(rival) * cfg.WarTrucePremium / 100f) * 100;

                    var warItem = new NativeItem(rival.Name,
                        $"~r~At war with you.~s~ They come {cfg.WarContestMultiplier:0.0}x as " +
                        $"often, bring {cfg.WarExtraEnforcers} more bodies, and will not take " +
                        $"the ordinary price. Beat them under {cfg.WarEndStrength:0} strength " +
                        $"and they stop on their own. Strength {rival.Strength:0}, holding {held}.")
                    {
                        AltTitle = $"~r~war  ${warCost:N0}",
                        Enabled = Game.Player.Money >= warCost
                    };

                    string warId = rival.Id;
                    warItem.Activated += (s, e) => BuyProtection(warId, cfg.WarTrucePremium);
                    _rivals.Add(warItem);
                    continue;
                }

                if (state.HasTruce(rival.Id))
                {
                    _rivals.Add(new NativeItem(rival.Name,
                        $"Paid up. They stay off your corners — unless you take one of theirs. " +
                        $"Strength {rival.Strength:0}, holding {held}.")
                    {
                        AltTitle = $"~g~peace {state.TruceDaysLeft(rival.Id)}d",
                        Enabled = false
                    });
                }
                else
                {
                    int cost = ProtectionCost(rival);
                    var item = new NativeItem(rival.Name,
                        $"Strength {rival.Strength:0}, aggression {rival.Aggression * 100:0}%, " +
                        $"holding {held}. Activate to pay ${cost:N0} for " +
                        $"{cfg.ProtectionDays} days of quiet." +
                        (rival.Aggression >= cfg.WarAggressionThreshold - 0.1f
                            ? " ~o~They are close to giving up on testing you.~s~"
                            : string.Empty))
                    {
                        AltTitle = $"${cost:N0}"
                    };

                    string id = rival.Id;
                    item.Activated += (s, e) => BuyProtection(id);
                    _rivals.Add(item);
                }

                // Alliance, on top of whatever their standing is.
                {
                    string why;
                    bool allowed = Systems.Rivals.CanAlly(rival, out why);
                    int allyCost = Systems.Rivals.AllianceCost(rival);
                    bool afford = Game.Player.Money >= allyCost;

                    var ally = new NativeItem($"  Bring {rival.Name} in",
                        allowed
                            ? $"${allyCost:N0} for {cfg.AllianceDays} days. They stop coming for " +
                              $"you entirely and send {cfg.AllySpawnCount} people to any corner " +
                              "you go and take off somebody else."
                            : why)
                    {
                        AltTitle = allowed ? $"~b~${allyCost:N0}" : "~r~—",
                        Enabled = allowed && afford
                    };

                    string allyId = rival.Id;
                    ally.Activated += (s, e) =>
                    {
                        var crew = GameState.Current.GetRival(allyId);
                        if (crew != null && Systems.Rivals.BuyAlliance(crew)) RebuildRivals();
                    };
                    _rivals.Add(ally);
                }
            }

            int rep = state.Reputation;
            int next = Reputation.NextRankAt(rep);

            _rivals.Add(new NativeItem("Your name",
                next < 0
                    ? "Nothing left to prove."
                    : $"{next - rep} more to the next rank. Turning up builds it; " +
                      "letting things happen without you costs it.")
            {
                AltTitle = $"{Reputation.Rank(rep)} ({rep})",
                Enabled = false
            });
        }

        /// <param name="premium">
        /// Multiplier on the price. Buying out of a war costs several times the
        /// ordinary rate — they are winning, or they think they are.
        /// </param>
        private void BuyProtection(string rivalId, float premium = 1f)
        {
            var state = GameState.Current;
            var rival = state.GetRival(rivalId);
            if (rival == null || rival.IsBroken) return;

            int cost = (int)Math.Round(ProtectionCost(rival) * premium / 100f) * 100;
            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~They want ${cost:N0} in hand.~s~ Nobody's taking a promise.");
                return;
            }

            Game.Player.Money -= cost;
            state.SetTruce(rivalId, Config.Current.ProtectionDays);

            bool wasWar = rival.IsAtWar;
            if (wasWar)
            {
                // Money ends the war, but it does not make them like you — the
                // aggression that started it is only knocked back, so a crew you
                // bought off once can tip over again.
                rival.WarSinceDay = 0;
                rival.Aggression *= 0.6f;
                rival.Clamp();
            }

            Notify.Show(wasWar
                ? $"~g~{rival.Name} took the money and stood down.~s~ " +
                  $"{Config.Current.ProtectionDays} days, and they still don't like you."
                : $"~g~{rival.Name} will leave your corners alone~s~ for " +
                  $"{Config.Current.ProtectionDays} days.", true);

            RebuildRivals();
        }
    }
}
