using UnityEngine;

namespace Eindwerk.PlayerMechanics
{
    /// <summary>Toont een korte uitleg van de besturing in de testomgeving.</summary>
    public sealed class MechanicsHud : MonoBehaviour
    {
        [SerializeField] private bool multiplayer;
        private GUIStyle panel;
        private GUIStyle text;

        /// <summary>Kiest de titel voor de gewone testscene of de multiplayerscene.</summary>
        public void Configure(bool isMultiplayer) => multiplayer = isMultiplayer;

        private bool SettingsVisible => !multiplayer && Cursor.lockState != CursorLockMode.Locked;

        private static Rect SettingsRect => new Rect(12f, 12f, Mathf.Min(320f, Screen.width - 24f), 124f);

        internal bool IsPointerOverSettings(Vector2 screenPosition)
        {
            return SettingsVisible && SettingsRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        }

        internal static void DrawMouseSensitivity()
        {
            int current = CapsulePlayerController.MouseSensitivityPercent;
            GUILayout.Label($"Muisgevoeligheid: {current}%");
            int selected = Mathf.RoundToInt(GUILayout.HorizontalSlider(current,
                CapsulePlayerController.MinimumMouseSensitivityPercent,
                CapsulePlayerController.MaximumMouseSensitivityPercent));
            if (GUILayout.Button("Standaardwaarde herstellen"))
                selected = CapsulePlayerController.DefaultMouseSensitivityPercent;
            if (selected != current)
                CapsulePlayerController.MouseSensitivityPercent = selected;
        }

        private void OnGUI()
        {
            if (panel == null)
            {
                panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12) };
                text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                text.normal.textColor = Color.white;
            }

            float width = Mathf.Min(540f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12, Screen.height - 156, width, 144), panel);
            GUILayout.Label(multiplayer ? "MAP  |  MULTIPLAYER TESTOMGEVING" : "PLAYER MECHANICA  |  TESTOMGEVING", text);
            GUILayout.Label("WASD / pijlen: bewegen   Shift: sprint   Spatie: springen\nCtrl / C: hurken   Muis: kijken   V: camera wisselen   R: terugzetten\nEsc: instellingen   Klik buiten het menu: besturing hervatten", text);
            GUILayout.Label("Test de trappen, helling, springblokken en lage doorgang. Geen spelregels.", text);
            GUILayout.EndArea();

            if (SettingsVisible)
            {
                GUILayout.BeginArea(SettingsRect, panel);
                GUILayout.Label("Instellingen", text);
                DrawMouseSensitivity();
                GUILayout.EndArea();
            }
        }
    }
}
