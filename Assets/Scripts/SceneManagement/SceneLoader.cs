﻿using System;
using System.Collections;
using System.Threading.Tasks;
using Eflatun.SceneReference;
using UI;
using UI.Transition;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Utility;
using World;

namespace SceneManagement
{
    /// <summary>
    /// Manages the loading of scenes and updates the loading UI.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        /// <summary>
        ///  Event triggered when a scene group is loaded.
        /// </summary>
        public Action<SceneGroup> OnLoadSceneGroup;
        
        /// <summary>
        /// Event triggered when a subscene is moved.
        /// </summary>
        public static Action<string> OnSubSceneMoved;
        
        /// <summary>
        /// The loading bar image.
        /// </summary>
        [SerializeField] private Image loadingBar;

        /// <summary>
        /// The speed at which the loading bar fills.
        /// </summary>
        [SerializeField] private float fillSpeed = 0.5f;

        /// <summary>
        /// The canvas displaying the loading screen.
        /// </summary>
        [SerializeField] private Canvas loadingCanvas;

        /// <summary>
        /// The camera used during the loading screen.
        /// </summary>
        [SerializeField] private Camera loadingCamera;

        /// <summary>
        /// The default UI scene to load.
        /// </summary>
        [SerializeField] private SceneReference defaultUIScene;

        /// <summary>
        /// Scene for the Ending.
        /// </summary>
        [SerializeField] SceneGroup EndScene;
        
        /// <summary>
        /// The array of scene groups to be loaded.
        /// </summary>
        [SerializeField] private SceneGroup[] SceneGroups;

        /// <summary>
        /// The active scene group.
        /// </summary>
        private SceneGroup _activeSceneGroup;

        /// <summary>
        /// The next scene group to load.
        /// </summary>
        private SceneGroup _nextSceneGroup;
        
        private float targetProgress;
        private bool isLoading;

        /// <summary>
        /// The scene group manager instance.
        /// </summary>
        public static readonly SceneGroupManager SceneGroupManager = new SceneGroupManager();

        /// <summary>
        /// Initializes the scene loader.
        /// </summary>
        private void Awake()
        {
            // SceneGroupManager.OnSceneLoad += sceneName => Debug.Log($"Loaded scene: {sceneName}");
            // SceneGroupManager.OnSceneUnload += sceneName => Debug.Log($"Unloaded scene: {sceneName}");
            // SceneGroupManager.OnSceneGroupLoaded += () => Debug.Log("Scene group loaded");

            // TODO: This race condition is not ideal. We should refactor this to be more robust.
            Bootstrapper.Instance.UpdateSceneLoader(this);
            OnLoadSceneGroup += StartLoadingSceneGroup;
        }
        
        private void StartLoadingSceneGroup(SceneGroup group)
        {
            StartCoroutine(LoadSceneGroup(group));
        }

        /// <summary>
        /// Starts the scene loading process.
        /// </summary>
        private void Start()
        {
            StartCoroutine(LoadSceneGroupIndex(1));
        }

        /// <summary>
        /// Updates the loading bar fill amount.
        /// </summary>
        private void Update()
        {
            if (!isLoading || !loadingBar) return;

            float currentFillAmount = loadingBar.fillAmount;
            float progressDiff = Mathf.Abs(currentFillAmount - targetProgress);

            float dynamicFillSpeed = progressDiff * fillSpeed;

            loadingBar.fillAmount = Mathf.Lerp(currentFillAmount, targetProgress, Time.deltaTime * dynamicFillSpeed);
        }

        /// <summary>
        /// Loads the scene group at the specified index.
        /// </summary>
        /// <param name="index">The index of the scene group to load.</param>
        public IEnumerator LoadSceneGroupIndex(int index)
        {
            if (loadingBar) loadingBar.fillAmount = 0f;
            targetProgress = 1f;

            if (index < 0 || index >= SceneGroups.Length)
            {
                Debug.LogError($"Invalid SceneGroup index: {index}");
                yield break;
            }

            yield return LoadSceneGroup(SceneGroups[index]);
        }

        /// <summary>
        /// Loads the specified scene group.
        /// </summary>
        /// <param name="group">The scene group to load.</param>
        private IEnumerator LoadSceneGroup(SceneGroup group)
        {
            yield return LoadSceneGroup(group, null);
        }
        
        private IEnumerator LoadSceneGroup(SceneGroup group, Action onComplete)
        {
            _activeSceneGroup = group;
            LoadingProgress progress = new LoadingProgress();
            progress.OnProgress += target => targetProgress = Mathf.Max(target, targetProgress);
            
            // Check if there is a UI Scene to load, otherwise insert default UI Scene
            if (string.IsNullOrWhiteSpace(group.FindSceneNameByType(SceneType.UserInterface)))
            {
                var uiScene = new SceneData()
                {
                    Reference = defaultUIScene,
                    SceneType = SceneType.UserInterface
                };
                group.Scenes.Add(uiScene);
            }
            
            yield return EnableLoadingCanvas();
            yield return SceneGroupManager.LoadScenes(group, progress);
            yield return EnableLoadingCanvas(false);
            onComplete?.Invoke();
        }

        /// <summary>
        /// Enables or disables the loading canvas.
        /// </summary>
        /// <param name="enable">If set to <c>true</c>, enables the loading canvas.</param>
        private IEnumerator EnableLoadingCanvas(bool enable = true)
        {
            isLoading = enable;
            if (loadingCanvas) loadingCanvas.gameObject.SetActive(enable);

            if (enable)
            {
                UIManager.Instance.OnNodeTransitionStart?.Invoke();
            }
            else
            {
                UIManager.Instance.OnNodeTransitionEnd?.Invoke();
            }
            // TODO: Have a timeout so we don't get stuck here
            var timeout = 0;
            var uiTransition = FindObjectOfType<UITransition>(); // If there is no UI Transition component, we can skip this
            while (uiTransition != null && UIManager.Instance.IsTransitioning && timeout++ < 100)
            {
                // Wait for the transition to complete
                yield return new WaitForSeconds(0.5f);
            }

            if (loadingCamera)
            {
                loadingCamera.gameObject.SetActive(enable);
                
                if (enable) Camera.SetupCurrent(loadingCamera);
            }
        }
        
        /// <summary>
        /// Reloads the currently active scene group.
        /// </summary>
        public IEnumerator ReloadActiveSceneGroup()
        {
            if (!_activeSceneGroup) yield break;
            yield return LoadSceneGroup(_activeSceneGroup);
        }

        private SceneGroup DetermineNextNode()
        {
            if (!_activeSceneGroup.IgnorePills && WorldManager.Instance.GetGameState().Player.HasConsumedPills)
            {
                return SceneGroups[0];
            }
            // Determine the next group from the available transitions, default to current group.
            // TODO: For now it will grab first avail, but there should be a way to priority / order these
            foreach (var transition in _activeSceneGroup.TransitionTargets)
            {
                if (transition.CanTransition())
                {
                    return transition.SceneGroup;
                }
            }
            
            return _activeSceneGroup;
        }

        /// <summary>
        /// Ends the current node.
        /// </summary>
        public void EndNode()
        {
            if (PlayerManager.Instance.IsGameEnded)
            {
                return;
            }
            // Determine the next node
            // TODO: Currently just reloading the active scene group
            _nextSceneGroup = DetermineNextNode();

            // Load the next node
            StartCoroutine(LoadSceneGroup(_nextSceneGroup));
        }

        private IEnumerator TransitionDelay(Action onComplete)
        {
            while (UIManager.Instance.IsTransitioning)
            {
                yield return null;
            }
            onComplete?.Invoke();
        }
        
        /// <summary>
        /// Gets the scene data for the specified subscene type.
        /// </summary>
        /// <param name="sceneType"></param>
        /// <returns></returns>
        public SceneData GetSubSceneData(SubSceneType sceneType)
        {
            return _activeSceneGroup.FindSceneBySubType(sceneType);
        }

        public void EndGame()
        {
            // Load the end scene
            StartCoroutine(LoadSceneGroup(EndScene));
        }
    }

    /// <summary>
    /// Reports the loading progress.
    /// </summary>
    public class LoadingProgress : IProgress<float>
    {
        /// <summary>
        /// Event triggered when the progress is updated.
        /// </summary>
        public event Action<float> OnProgress;

        private const float ratio = 1f;

        /// <summary>
        /// Reports the progress value.
        /// </summary>
        /// <param name="value">The progress value.</param>
        public void Report(float value)
        {
            OnProgress?.Invoke(value / ratio);
        }
    }
}