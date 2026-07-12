using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SafetyProto.Utils
{
    [RequireComponent(typeof(Camera))]
    public class ScreenshotCapture : MonoBehaviour
    {
        [SerializeField] private int width = 3840;
        [SerializeField] private int height = 2160;
        [SerializeField] private Key captureKey = Key.F12;
        [SerializeField] private string outputFolder = "Screenshots";
        [SerializeField] private string filePrefix = "screenshot";

        private Camera _cam;

        private Camera Cam => _cam != null ? _cam : (_cam = GetComponent<Camera>());

        private void Update()
        {
            // Input System, not the legacy Input class: the project runs with
            // activeInputHandler = "Input System Package (New)", under which
            // Input.GetKeyDown throws. Keyboard.current is null in a headset
            // session with no keyboard attached, hence the guard.
            var keyboard = Keyboard.current;
            if (keyboard == null || captureKey == Key.None) return;

            if (keyboard[captureKey].wasPressedThisFrame)
                Capture();
        }

        [ContextMenu("Take Screenshot")]
        public void Capture()
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;

            var prev = Cam.targetTexture;
            Cam.targetTexture = rt;
            Cam.Render();
            Cam.targetTexture = prev;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            SafeDestroy(rt);

            string folder = Path.Combine(Application.dataPath, "..", outputFolder);
            Directory.CreateDirectory(folder);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(folder, $"{filePrefix}_{timestamp}.png");

            File.WriteAllBytes(path, tex.EncodeToPNG());
            SafeDestroy(tex);

            Debug.Log($"[ScreenshotCapture] Saved {width}x{height} → {Path.GetFullPath(path)}");
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(obj); return; }
#endif
            Destroy(obj);
        }
    }
}

