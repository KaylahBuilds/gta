using System;
using System.Runtime.CompilerServices;
using iFruitAddon2;
using OnTheBlade.Core;
using OnTheBlade.Persistence;

namespace OnTheBlade.UI
{
    /// <summary>
    /// Adds an "On The Blade" contact to the in-game phone.
    ///
    /// Every reference to iFruitAddon2 is confined to this file, and the two
    /// methods that touch its types are marked NoInlining and only ever called
    /// from inside a try/catch. The CLR loads an assembly when it JITs a method
    /// that needs it — so if iFruitAddon2.dll is missing or incompatible, the
    /// failure lands in that catch and the feature turns itself off, instead of
    /// stopping the whole mod from loading.
    ///
    /// That matters more than usual here: this mod ships one binary for two game
    /// editions, and a third-party dependency is the most likely thing to break
    /// on one of them after a patch.
    /// </summary>
    public class PhoneBridge
    {
        private readonly Action _onAnswered;
        private object _fruit;

        public bool Available { get; private set; }

        public PhoneBridge(Action onAnswered)
        {
            _onAnswered = onAnswered;
        }

        public void Initialise()
        {
            if (!Config.Current.EnablePhoneContact)
            {
                SaveManager.Log("Phone contact disabled in config.");
                return;
            }

            try
            {
                InitCore();
                Available = true;
                SaveManager.Log("Phone contact registered.");
            }
            catch (Exception ex)
            {
                Available = false;
                SaveManager.Log(
                    $"Phone contact unavailable ({ex.GetType().Name}: {ex.Message}). " +
                    "Continuing without it — the menu keybind still works.");
            }
        }

        public void Update()
        {
            if (!Available) return;

            try
            {
                UpdateCore();
            }
            catch (Exception ex)
            {
                Available = false;
                SaveManager.Log($"Phone contact disabled after error: {ex.Message}");
            }
        }

        // --- everything below touches iFruitAddon2 types ------------------

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InitCore()
        {
            var fruit = new CustomiFruit();

            var contact = new iFruitContact("On The Blade")
            {
                Active = true,
                DialTimeout = 2500
            };
            contact.Answered += _ => _onAnswered();

            fruit.Contacts.Add(contact);
            _fruit = fruit;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void UpdateCore()
        {
            ((CustomiFruit)_fruit)?.Update();
        }
    }
}
