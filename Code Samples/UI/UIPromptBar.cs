using System.Collections.Generic;
using UnityEngine;

namespace Gamble.BattleCards.UI
{
    /// <summary>
    /// Manages the prompts displayed at the bottom of the screen
    /// </summary>
    public class UIPromptBar : MonoBehaviour
    {
        [SerializeField] private UIPrompt promptPrefab;

        [SerializeField] private RectTransform leftContainer;
        [SerializeField] private RectTransform rightContainer;

        private List<UIPrompt> currentPrompts;

        private void OnEnable()
        {
            UIMenuBase.onMenuFocused += OnMenuOpened;
            UIMenuBase.onMenuInputToggled += OnMenuInputToggled;
        }

        private void OnDisable()
        {
            UIMenuBase.onMenuFocused -= OnMenuOpened;
            UIMenuBase.onMenuInputToggled -= OnMenuInputToggled;
        }

        private void OnMenuOpened(UIMenuBase menu)
        {
            currentPrompts ??= new List<UIPrompt>();

            ClearPrompts();
            LoadPrompts(menu.GetMenuPrompts());
        }

        private void OnMenuInputToggled(bool enabled)
        {
            if (!currentPrompts.HasElements())
                return;

            foreach (UIPrompt prompt in currentPrompts)
            {
                prompt.ToggleInteractable(enabled);
            }
        }

        private void LoadPrompts(List<UIPrompt.PromptDetails> promptDetails)
        {
            foreach (UIPrompt.PromptDetails details in promptDetails)
            {
                UIPrompt prompt = Instantiate(promptPrefab, details.isLeft ? leftContainer : rightContainer);
                prompt.Init(details);

                currentPrompts.Add(prompt);
            }
        }

        private void ClearPrompts()
        {
            if (!currentPrompts.HasElements())
                return;

            foreach (UIPrompt prompt in currentPrompts)
            {
                prompt.CleanUp();
            }

            currentPrompts.Clear();
        }
    }
}
