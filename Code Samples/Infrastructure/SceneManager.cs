using Gamble.BattleCards.Logging;
using Gamble.BattleCards.UI;
using Gamble.Utils;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamble.BattleCards.Infrastructure
{
    public class SceneManager : Singleton<SceneManager>
    {
        public override IEnumerator Initalise()
        {
            LoadScene("MainMenu", UnityEngine.SceneManagement.LoadSceneMode.Single, () =>
            {
                initialised = true;
            });
            
            return base.Initalise();
        }

        /// <summary>
        /// Load a new scene
        /// </summary>
        /// <param name="sceneName"> The name of the scene. </param>
        /// <param name="loadMode"> The load mode to use. </param>
        /// <param name="onComplete"> Action to fire when loading is completed. </param>
        public void LoadScene(string sceneName, LoadSceneMode loadMode, Action onComplete = null)
        {
            // Open the loading the UI
            UILoading.ShowScreen(async () =>
            {
                LOG.Log($"Loading Scene: {sceneName}", LOG.Type.SCENE);

                AsyncOperation operation = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, loadMode);
                operation.allowSceneActivation = true;

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                LOG.Log($"Scene Loaded: {sceneName}", LOG.Type.SCENE);

                onComplete?.Invoke();

                // Hide the loading screen
                UILoading.HideScreen();
            });
        }
    }
}
