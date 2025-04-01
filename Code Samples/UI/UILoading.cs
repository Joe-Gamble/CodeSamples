using Gamble.BattleCards.Logging;
using Gamble.Utils;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Gamble.BattleCards.UI
{
    /// <summary>
    /// Handles behaviours pertaining to the UI loaading screen and loading indicator
    /// </summary>
    public class UILoading : Singleton<UILoading>
    {
        [SerializeField] private UIRadialIndicator indicator;
        [SerializeField] private UILoadingTips loadingTips;

        [SerializeField] private CanvasGroup backgroundCanvas;
        [SerializeField] private CanvasGroup indicatorCanvas;

        private static bool isShowingScreen = false;
        private static List<Func<bool>> loadTasks = new List<Func<bool>>();

        private static CancellationTokenSource _cts;

        private void OnDestroy()
        {
            Cleanup();
        }

        public static void ShowScreen(Action onComplete = null)
        {
            if (isShowingScreen)
            {
                onComplete?.Invoke();
                return;
            }

            Instance.loadingTips.gameObject.SetActive(true);

            HelperFunctions.FadeCanvasAsync(Instance.backgroundCanvas, true, 1, () =>
            {
                ShowIndicator(() => !isShowingScreen);
                Instance.loadingTips.DisplayTips();

                isShowingScreen = true;
                onComplete?.Invoke();
            });
        }

        public static void HideScreen(Action onComplete = null)
        {
            if (!isShowingScreen)
            {
                onComplete?.Invoke();
                return;
            }

            isShowingScreen = false;

            HelperFunctions.FadeCanvasAsync(Instance.backgroundCanvas, false, 1, () =>
            {
                Instance.loadingTips.StopTips();
                onComplete?.Invoke();
            });
        }

        public static void ShowIndicator(Func<bool> task)
        {
            _cts ??= new CancellationTokenSource();

            loadTasks.Add(task);

            if (loadTasks.Count == 1)
            {
                Instance.indicator.StartIndicator();
                HelperFunctions.FadeCanvasAsync(Instance.indicatorCanvas, true, 1);
            }

            Task.Run(() => RunTask(task, _cts.Token), _cts.Token);
        }

        private static async Task RunTask(Func<bool> func, CancellationToken token)
        {
            bool taskCompleted = false;

            while (!taskCompleted)
            {
                if (token.IsCancellationRequested)
                {
                    loadTasks.Remove(func);

                    if (loadTasks.Count == 0) // Stop indicator if no tasks left
                    {
                        HideIndicator();
                    }
                }

                token.ThrowIfCancellationRequested();

                await Awaitable.MainThreadAsync();

                taskCompleted = func.Invoke();

                if (taskCompleted)
                {
                    loadTasks.Remove(func);

                    if (loadTasks.Count == 0) // Stop indicator if no tasks left
                    {
                        HideIndicator();
                    }
#if UNITY_EDITOR                    
                    //string methodName = func.Method.Name;

                    //if (methodName.Contains("<") && methodName.Contains(">"))
                    //{
                    //    int start = methodName.IndexOf("<") + 1;
                    //    int end = methodName.IndexOf(">");
                    //    methodName = methodName[start..end];
                    //}

                    //LOG.Log($"Task completed. Method: {methodName}()", LOG.Type.UI);
#endif
                }

                await Awaitable.BackgroundThreadAsync();

                if (taskCompleted)
                    await Task.CompletedTask;

                await Task.Delay(200);
                await Task.Yield();
            }
        }

        private static void HideIndicator()
        {
            HelperFunctions.FadeCanvasAsync(Instance.indicatorCanvas, false, 1, () =>
            {
                Instance.indicator.StopIndicator();
            });
        }

        private static void Cleanup()
        {
            _cts?.Cancel();
            loadTasks.Clear();
            _cts = null;
        }
    }
}
