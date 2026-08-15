using System.Collections.Generic;
using System.Linq;
using GTA;

namespace OnTheBlade.Core
{
    public class LoadoutDef
    {
        public string Id;
        public string Name;
        public string Description;
        public int Cost;

        public WeaponHash Weapon;
        public int Ammo;

        /// <summary>Body armour the enforcer spawns with, 0-100.</summary>
        public int Armour;

        /// <summary>Ped accuracy, 0-100. Rival crews sit at 25-30 for reference.</summary>
        public int Accuracy;

        /// <summary>
        /// Added to the enforcer's skill when rolling deterrence and client
        /// trouble. An armed man talks people down that an unarmed one does not,
        /// so the weapon matters even on the nights you never see him.
        /// </summary>
        public int Muscle;

        /// <summary>
        /// What it costs to send him out with it, per fight he handles without
        /// you. Buying the best gun once should not be a permanent solution to a
        /// recurring problem — a carbine that holds a corner every night has a
        /// running cost, and that is what keeps the loadout a decision rather
        /// than a purchase.
        /// </summary>
        public int AmmoCost;
    }

    /// <summary>
    /// What you can put in an enforcer's hands.
    ///
    /// Until now muscle was pure probability — no ped, no presence, nothing to
    /// watch. Arming one changes that: an enforcer with a weapon turns up in
    /// person at incidents in the region he covers and fights whoever is there,
    /// gang or client.
    ///
    /// He still does not win it for you. The original rule holds — if muscle
    /// could settle a turf battle alone there would be no reason to drive out —
    /// so this is backup, not a replacement. What it buys is not having to do it
    /// on your own, and it can cost you the man if it goes badly.
    /// </summary>
    public static class Armoury
    {
        /// <summary>The id stored on an enforcer who has never been armed.</summary>
        public const string Unarmed = "";

        public const string Bat = "bat";
        public const string Pistol = "pistol";
        public const string Smg = "smg";
        public const string Carbine = "carbine";

        public static readonly List<LoadoutDef> All = new List<LoadoutDef>
        {
            new LoadoutDef
            {
                Id = Bat, Name = "A bat", Cost = 600, AmmoCost = 0,
                Weapon = WeaponHash.Bat, Ammo = 0,
                Armour = 0, Accuracy = 20, Muscle = 8,
                Description = "He turns up, and he is holding something. Enough for a client " +
                              "who has had a few. Not enough for a crew with pistols."
            },
            new LoadoutDef
            {
                Id = Pistol, Name = "A pistol", Cost = 3000, AmmoCost = 150,
                Weapon = WeaponHash.Pistol, Ammo = 250,
                Armour = 25, Accuracy = 35, Muscle = 18,
                Description = "Matches what the crews carry. He can hold his own at a corner " +
                              "and he will not go down to the first shot."
            },
            new LoadoutDef
            {
                Id = Smg, Name = "A micro SMG", Cost = 9000, AmmoCost = 450,
                Weapon = WeaponHash.MicroSMG, Ammo = 400,
                Armour = 50, Accuracy = 45, Muscle = 30,
                Description = "Outguns anything a crew brings to a corner. Half armour, and " +
                              "he stops being the one who dies first."
            },
            new LoadoutDef
            {
                Id = Carbine, Name = "A carbine", Cost = 22000, AmmoCost = 900,
                Weapon = WeaponHash.CarbineRifle, Ammo = 500,
                Armour = 100, Accuracy = 55, Muscle = 45,
                Description = "Full armour and a rifle. He will clear a corner on his own if " +
                              "you are slow getting there — and he is expensive to lose."
            }
        };

        public static LoadoutDef Get(string id)
        {
            return string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(l => l.Id == id);
        }

        public static string NameOf(string id) => Get(id)?.Name ?? "Bare hands";

        /// <summary>Skill bonus from whatever he is carrying. Zero if unarmed.</summary>
        public static int MuscleOf(string id) => Get(id)?.Muscle ?? 0;

        /// <summary>
        /// Loadouts are ordered by cost, so "better than what he has" is just a
        /// higher index. Returning -1 for unarmed makes every real loadout an
        /// upgrade without a special case.
        /// </summary>
        public static int RankOf(string id)
        {
            var def = Get(id);
            return def == null ? -1 : All.IndexOf(def);
        }
    }
}
