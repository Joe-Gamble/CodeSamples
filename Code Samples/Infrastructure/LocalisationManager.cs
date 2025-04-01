using Gamble.BattleCards.Logging;
using Gamble.Utils;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Gamble.BattleCards.Infrastructure
{
    public class LocalisationManager : Singleton<LocalisationManager>
    {
        public const string DEFAULT_LOCALE = "en";
        public Action OnLanguageChanged;

        private LocalizationSettings settings = null;
        private StringTable stringTable = null;

        public override IEnumerator Initalise()
        {
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            SaveSystem.Instance.AccessibilitySettings.localeTag.onValueChanged += OnLocalisationTagChanged;

            SetLocalisationLocale(SaveSystem.Instance.AccessibilitySettings.localeTag.Value, () =>
            {
                initialised = true;
            });

            yield return base.Initalise();
        }

        private void SetLocalisationLocale(string tag, Action onComplete = null)
        {
            AsyncOperationHandle<LocalizationSettings> handle = LocalizationSettings.InitializationOperation;

            handle.Completed += (handle) =>
            {
                settings = handle.Result;
                ILocalesProvider locales = settings.GetAvailableLocales();

                Locale locale = locales.GetLocale(tag);

                if (locale == null)
                {
                    locale = locales.GetLocale(DEFAULT_LOCALE);

                    SaveSystem.Instance.AccessibilitySettings.localeTag.Value = DEFAULT_LOCALE;
                    SaveSystem.Instance.AccessibilitySettings.Save(() =>
                    {
                        settings.SetSelectedLocale(locale);
                        UpdateLocaleTable(locale, () => onComplete?.Invoke());
                    });
                }
                else
                {
                    settings.SetSelectedLocale(locale);
                    UpdateLocaleTable(locale, () => onComplete?.Invoke());
                }

                LOG.Log($"Localisation updated. New Locale: {settings.GetSelectedLocale().Identifier.Code}", LOG.Type.SYSTEM);
            };
        }

        private void OnLocalisationTagChanged(string tag)
        {
            if (settings != null)
            {
                SetLocalisationLocale(tag);
            }
        }

        private void OnLocaleChanged(Locale locale)
        {
            UpdateLocaleTable(locale);
        }

        private void UpdateLocaleTable(Locale locale, Action onComplete = null)
        {
            try
            {
                AsyncOperationHandle<StringTable> handle = LocalizationSettings.StringDatabase.GetTableAsync("Text Localisation", locale);

                handle.Completed += (handle) =>
                {
                    if (handle.IsValid() && handle.Result != null)
                    {
                        stringTable = handle.Result;
                        OnLanguageChanged?.Invoke();
                    }

                    onComplete?.Invoke();
                };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public bool TryGetLocalisaionSafe(string key, out string translation)
        {
            StringTableEntry tableEntry = stringTable.GetEntry(key);

            if (tableEntry != null)
            {
                translation = tableEntry.GetLocalizedString();
                return true;
            }

            translation = default;
            return false;
        }
    }
}
