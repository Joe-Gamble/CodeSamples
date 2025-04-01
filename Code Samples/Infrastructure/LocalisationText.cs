using Gamble.BattleCards.Infrastructure;
using TMPro;
using UnityEngine;

namespace Gamble.BattleCards
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class LocalisationText : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI textElement;
        [SerializeField] private string defaultText;
        [SerializeField] private string localisationKey;

        private void OnEnable()
        {
            LocalisationManager.Instance.OnLanguageChanged += LocaliseText;

            LocalisationManager.Instance.NotifyOnInitialised(() =>
            {
                LocaliseText();
            });
        }

        public void SetKey(string key)
        {
            localisationKey = key;
            LocaliseText();
        }

        private void LocaliseText()
        {
            if (string.IsNullOrEmpty(localisationKey))
                return;

           if (LocalisationManager.Instance.TryGetLocalisaionSafe(localisationKey, out string translatedText))
           {
                textElement.text = translatedText;
           }
           else
           {
                textElement.text = defaultText;
           }
        }

        private void OnDisable()
        {
            LocalisationManager.Instance.OnLanguageChanged -= LocaliseText;
        }


    }
}
