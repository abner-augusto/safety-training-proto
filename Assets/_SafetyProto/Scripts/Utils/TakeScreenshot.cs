using UnityEngine;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class HiResShot : MonoBehaviour {
    public Camera cam;
    public int width = 3840;
    public int height = 2160;

    [ContextMenu("capture hi-res")]
    void Capture() {
        // cria render target
        var rt = new RenderTexture(width, height, 24);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;

        // limpa render texture
#if UNITY_EDITOR
        DestroyImmediate(rt);
#else
            Destroy(rt);
#endif

        // cria path
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string dirPath = Path.Combine(Application.dataPath, "_SafetyProto/Screenshots");
        string filename = $"screenshot_{timestamp}.png";
        string fullPath = Path.Combine(dirPath, filename);

        // garante que o diretório existe
        if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

        // salva imagem
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        Debug.Log($"screenshot salvo em: {fullPath}");
    }
}