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
            GUILayout.Label("WASD / pijlen: bewegen   Shift: sprint   Spatie: springen\nCtrl / C: hurken   Muis: kijken   V: camera wisselen   R: terugzetten\nEsc: muis vrij   Klik: besturing hervatten", text);
            GUILayout.Label("Test de trappen, helling, springblokken en lage doorgang. Geen spelregels.", text);
            GUILayout.EndArea();
        }
    }
}
