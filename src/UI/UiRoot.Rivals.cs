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

                if (state.HasTruce(rival.Id))
                {
                    _rivals.Add(new NativeItem(rival.Name,
                        $"Paid up. They stay off your corners — unless you take one of theirs. " +
                        $"Strength {rival.Strength:0}, holding {held}.")
                    {
                        AltTitle = $"~g~peace {state.TruceDaysLeft(rival.Id)}d",
                        Enabled = false
                    });
                    continue;
                }

                int cost = ProtectionCost(rival);
                var item = new NativeItem(rival.Name,
                    $"Strength {rival.Strength:0}, aggression {rival.Aggression * 100:0}%, " +
                    $"holding {held}. Activate to pay ${cost:N0} for " +
                    $"{Config.Current.ProtectionDays} days of quiet.")
                {
                    AltTitle = $"${cost:N0}"
                };

                string id = rival.Id;
                item.Activated += (s, e) => BuyProtection(id);
                _rivals.Add(item);
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

        private void BuyProtection(string rivalId)
        {
            var state = GameState.Current;
            var rival = state.GetRival(rivalId);
            if (rival == null || rival.IsBroken) return;

            int cost = ProtectionCost(rival);
            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~They want ${cost:N0} in hand.~s~ Nobody's taking a promise.");
                return;
            }

            Game.Player.Money -= cost;
            state.SetTruce(rivalId, Config.Current.ProtectionDays);

            Notify.Show(
                $"~g~{rival.Name} will leave your corners alone~s~ for " +
                $"{Config.Current.ProtectionDays} days.", true);

            RebuildRivals();
        }
    }
}
