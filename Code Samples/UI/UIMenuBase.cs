using Gamble.BattleCards.Infrastructure.Input;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gamble.BattleCards.UI
{
    public class UIMenuBase : MonoBehaviour
    {
        [SerializeField] private bool activeOnEnable = false;
        [SerializeField] private Selectable initialSelection;

        private UIMenuBase previousMenu;
        private bool isFocused = false;
        private bool hideOnUnfocus = true;
        private bool inputEnabled = true;

        static public Action<UIMenuBase> onMenuFocused;
        static public Action<bool> onMenuInputToggled;

        protected virtual void OnEnable()
        {
            ToggleInput(true);

            if (isFocused)
                return;

            if (activeOnEnable)
                FocusMenu();
        }

        protected virtual void OnDisable()
        {
            if (!isFocused)
                return;

            UnFocusMenu();
        }

        public void OpenMenu(UIMenuBase menu)
        {
            if (menu == this)
                return;

            menu.SetPreviousMenu(this);
            menu.Show(hideOnUnfocus);
        }

        private void Show(bool hidePrevious = true, bool isBack = false)
        {
            if (previousMenu != null && !isBack)
            {
                if (hidePrevious)
                    previousMenu.Hide();
                else
                    previousMenu.UnFocusMenu();
            }

            if (!this.gameObject.activeInHierarchy)
            {
                this.gameObject.SetActive(true);
            }

            FocusMenu();
        }

        private void Hide()
        {
            UnFocusMenu();

            if (this.gameObject.activeInHierarchy)
            {
                this.gameObject.SetActive(false);
            }
        }

        public virtual void FocusMenu()
        {
            isFocused = true;
            hideOnUnfocus = true;

            onMenuFocused?.Invoke(this);

            InputSystem.onDeviceChange += OnDeviceChanged;

            if (activeOnEnable)
            {
                if (Gamepad.current != null)
                    EventSystem.current.SetSelectedGameObject(initialSelection.gameObject);
            }
        }

        public virtual void UnFocusMenu()
        {
            isFocused = false;

            InputSystem.onDeviceChange -= OnDeviceChanged;

            if (EventSystem.current != null && Gamepad.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        protected virtual void ToggleInput(bool enabled)
        {
            inputEnabled = enabled;
            onMenuInputToggled?.Invoke(enabled);
        }

        public void SetHideFlag(bool hide)
        {
            hideOnUnfocus = hide;
        }

        public List<UIPrompt.PromptDetails> GetMenuPrompts()
        {
            List<UIPrompt.PromptDetails> details = new List<UIPrompt.PromptDetails>();
            GetMenuPrompts(ref details);

            return details;
        }

        protected virtual void GetMenuPrompts(ref List<UIPrompt.PromptDetails> details)
        {
            if (previousMenu != null)
            {
                details.Add(new UIPrompt.PromptDetails
                {
                    isLeft = false,
                    isButton = true,
                    onClick = () =>
                    {
                        Back();
                    },
                    localisationTag = "back",
                    hasActionImage = true,
                    actionKey = "Cancel"
                });
            }
        }

        public void SetPreviousMenu(UIMenuBase menu)
        {
            previousMenu = menu;
        }

        protected virtual void ProcessInput(MenuNavigationData data)
        {
            if (data.back && inputEnabled)
            {
                Back();
            }
        }

        public void Back()
        {
            if (previousMenu != null)
            {
                Hide();
                previousMenu.Show(isBack: true);
                previousMenu = null;
            }
        }

        private void Update()
        {
            if (isFocused)
            {
                MenuNavigationData data = InputManager.Instance.GetMenuNavigationData();
                ProcessInput(data);
            }
        }

        private void OnDeviceChanged(InputDevice device, InputDeviceChange status)
        {
            if (device is Gamepad)
            {
                switch (status)
                {
                    case InputDeviceChange.Added:
                    case InputDeviceChange.Enabled:
                    case InputDeviceChange.Reconnected:
                        EventSystem.current.SetSelectedGameObject(initialSelection.gameObject);
                        break;
                    case InputDeviceChange.Removed:
                    case InputDeviceChange.Disabled:
                    case InputDeviceChange.Disconnected:
                        EventSystem.current.SetSelectedGameObject(null);
                        break;
                }
            }
        }
    }
}
