using Game;
using OriBFArchipelago.Core;
using OriBFArchipelago.MapTracker.Core;
using System;
using System.Collections;
using UnityEngine;
using CoreInput = Core.Input;

namespace OriBFArchipelago.ArchipelagoUI.Feedback
{
    public class FeedbackWindow : MonoBehaviour
    {
        private Action<string> onSendCallback;
        private Action onCloseCallback;
        private string feedbackText = "";
        private Vector2 scrollPosition;
        private Rect windowRect;
        private bool isSending = false;
        private string statusMessage = "";
        private bool isConnected = false;
        private bool isInGame = false;

        public static bool IsActive { get; private set; }
        private bool closedByUser = false;

        private const string GithubUrl = "https://github.com/c-ostic/OriBFArchipelago/issues";

        private void Awake()
        {
            windowRect = new Rect(
                Screen.width / 2 - 350,
                Screen.height / 2 - 250,
                700,
                500
            );
            IsActive = true;
        }

        public void Initialize(Action<string> sendCallback, Action closeCallback = null)
        {
            onSendCallback = sendCallback;
            onCloseCallback = closeCallback;
        }

        private void OnDestroy()
        {
            IsActive = false;
            // Only call the callback if we closed cleanly (not by user pressing escape)
            if (!closedByUser)
            {
                onCloseCallback?.Invoke();
            }
        }

        private void Update()
        {
            // Check for escape key or cancel button to close the window
            if (CoreInput.Cancel.OnPressed)
            {
                CoreInput.Cancel.Used = true;
                closedByUser = true;
                onCloseCallback?.Invoke();
                Destroy(gameObject);
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                closedByUser = true;
                onCloseCallback?.Invoke();
                Destroy(gameObject);
            }
        }

        private void OnGUI()
        {
            // Update connection status
            UpdateStatus();

            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            windowRect = GUI.Window(12345, windowRect, DrawWindow, "Report In-Game Issue");
        }

        private void UpdateStatus()
        {
            isConnected = RandomizerManager.Connection != null && RandomizerManager.Connection.Connected;
            isInGame = Characters.Sein != null && Characters.Sein.Active;
        }

        private void DrawWindow(int windowID)
        {
            GUILayout.BeginVertical();

            // Header
            GUILayout.Label("Report In-Game Issues", GUI.skin.label);
            GUILayout.Space(5);

            // Explanation
            GUILayout.Label("Report map tracker or logic problems here.", GUI.skin.label);
            GUILayout.Space(10);

            // Connection Status
            if (!isConnected)
            {
                GUILayout.Label("Status: Not connected to Archipelago", GUI.skin.label);
            }
            else if (!isInGame)
            {
                GUILayout.Label("Status: Not in game", GUI.skin.label);
            }
            else
            {
                GUILayout.Label("Status: Ready to send", GUI.skin.label);
            }
            GUILayout.Space(10);

            // Text area
            GUILayout.Label("Describe the problem:", GUI.skin.label);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(180));
            bool canEditText = !isSending && isConnected && isInGame;
            GUI.enabled = canEditText;
            feedbackText = GUILayout.TextArea(feedbackText, GUILayout.ExpandHeight(true));
            GUI.enabled = true;
            GUILayout.EndScrollView();
            GUILayout.Space(10);

            // Status
            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUILayout.Label(statusMessage, GUI.skin.label);
                GUILayout.Space(5);
            }

            // Buttons
            GUILayout.BeginHorizontal();
            bool canSend = !isSending && isConnected && isInGame && !string.IsNullOrEmpty(feedbackText);
            GUI.enabled = canSend;
            if (GUILayout.Button(isSending ? "Sending..." : "Send Report", GUILayout.Height(40)))
            {
                isSending = true;
                statusMessage = "Sending report...";
                onSendCallback?.Invoke(feedbackText);
            }
            GUI.enabled = true;

            if (GUILayout.Button("Give Suggestion (opens in browser)", GUILayout.Height(40)))
            {
                Application.OpenURL(GithubUrl);
            }

            if (GUILayout.Button("Cancel", GUILayout.Height(40)))
            {
                Destroy(gameObject);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}