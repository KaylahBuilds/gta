using System;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// Money spent on one specific person. Split from the main UiRoot file purely
    /// to keep both readable — it is one class.
    ///
    /// Everything in here is bound to <see cref="_boundWorkerId"/>, which the
    /// roster sets before opening the detail menu this hangs off.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _invest;

        private void BuildInvestMenu()
        {
            _invest = new NativeMenu("On The Blade", "INVEST");
            _pool.Add(_invest);
            _invest.Shown += (s, e) => RebuildInvest();
        }

        private void RebuildInvest()
        {
            _invest.Clear();

            var worker = GameState.Current.GetWorker(_boundWorkerId);
            if (worker == null)
            {
                _invest.Add(new NativeItem("(nobody selected)",
                    "Pick someone from the roster first."));
                return;
            }

            _invest.Name = worker.Name.ToUpperInvariant();
            var cfg = Config.Current;

            // --- wardrobe ---
            if (Progression.IsMaxTier(worker))
            {
                _invest.Add(new NativeItem("Wardrobe", "She is tier 3. There is nowhere above this.")
                {
                    AltTitle = "~g~Tier 3",
                    Enabled = false
                });
            }
            else
            {
                int next = Progression.NextTier(worker);
                int cost = cfg.WardrobeCostFor(next);

                string reason;
                bool allowed = Progression.CanWardrobe(worker, out reason);
                bool afford = Game.Player.Money >= cost;

                int opens = 0;
                foreach (var z in Zones.All)
                    if (z.MinTier > worker.Tier && z.MinTier <= next) opens++;

                _invest.Add(Buy(
                    "Wardrobe",
                    allowed
                        ? $"Takes her to tier {next} now, instead of waiting out " +
                          $"{Progression.HoursNeeded(worker)} hours on the street. " +
                          (opens > 0
                              ? $"Opens {opens} more corner(s) and raises her hourly rate to " +
                                $"${cfg.BaseRateFor(next):N0}."
                              : $"Raises her hourly rate to ${cfg.BaseRateFor(next):N0}.")
                        : reason,
                    cost, allowed, afford,
                    () => { if (Progression.BuyWardrobe(worker)) RebuildInvest(); }));
            }

            // --- clinic ---
            {
                string reason;
                bool allowed = Progression.CanClinic(worker, out reason);
                bool afford = Game.Player.Money >= cfg.ClinicCost;

                _invest.Add(Buy(
                    "A doctor and a week off",
                    allowed
                        ? $"Stamina straight back to 100 and +{cfg.ClinicLoyalty:0} loyalty. " +
                          $"She is at {worker.Stamina:0} stamina, {worker.Loyalty:0} loyalty."
                        : reason,
                    cfg.ClinicCost, allowed, afford,
                    () => { if (Progression.BuyClinic(worker)) RebuildInvest(); }));
            }

            // --- studio ---
            {
                string reason;
                bool allowed = Progression.CanStudio(worker, out reason);
                bool afford = Game.Player.Money >= cfg.StudioCost;

                _invest.Add(Buy(
                    "Studio time",
                    allowed
                        ? $"Her followers build {(cfg.StudioFollowerBonus - 1f) * 100:0}% faster, " +
                          "permanently. Stacks with the ring light if she is Camera-ready."
                        : reason,
                    cfg.StudioCost, allowed, afford,
                    () => { if (Progression.BuyStudio(worker)) RebuildInvest(); },
                    ownedLabel: worker.HasStudio ? "~g~Owned" : null));
            }

            // --- papers ---
            {
                string reason;
                bool allowed = Progression.CanPapers(worker, out reason);
                bool afford = Game.Player.Money >= cfg.PapersCost;

                _invest.Add(Buy(
                    "Papers and a name",
                    allowed
                        ? "Buys her the Connected trait — she draws 40% less heat wherever " +
                          "you post her. The cheapest way to work a hot corner."
                        : reason,
                    cfg.PapersCost, allowed, afford,
                    () => { if (Progression.BuyPapers(worker)) RebuildInvest(); },
                    ownedLabel: (worker.TraitSet & WorkerTrait.Connected) != 0 ? "~g~Connected" : null));
            }

            // --- what it has cost so far ---
            _invest.Add(new NativeItem("Cash in hand", "What you have to spend right now.")
            {
                AltTitle = $"${Game.Player.Money:N0}",
                Enabled = false
            });
        }

        /// <summary>
        /// One purchase row. Disabled entries keep their reason in the description
        /// rather than just greying out — a menu that refuses without saying why is
        /// the thing players report as a bug.
        /// </summary>
        private static NativeItem Buy(string title, string description, int cost,
                                      bool allowed, bool afford, Action onBuy,
                                      string ownedLabel = null)
        {
            bool enabled = allowed && afford;

            var item = new NativeItem(title,
                enabled || !allowed
                    ? description
                    : description + $"  |  ~r~You need ${cost:N0}.")
            {
                AltTitle = ownedLabel ?? (allowed ? $"${cost:N0}" : "~r~—"),
                Enabled = enabled
            };

            if (enabled) item.Activated += (s, e) => onBuy();
            return item;
        }
    }
}
