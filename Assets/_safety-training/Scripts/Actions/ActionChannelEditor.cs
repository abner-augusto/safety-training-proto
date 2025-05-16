
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace SafetyTraining.Actions.Editor
{
    public static class ActionChannelCreator
    {
        private const string AssetPath = "Assets/_safety-training/Data/ActionChannel.asset";

        [MenuItem("Safety Training/Create/Action Channel Asset")]
        public static void CreateChannelAsset()
        {
            var channel = AssetDatabase.LoadAssetAtPath<ActionChannel>(AssetPath);
            if (channel == null)
            {
                channel = ScriptableObject.CreateInstance<ActionChannel>();
                AssetDatabase.CreateAsset(channel, AssetPath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = channel;
                Debug.Log($"Created new ActionChannel at {AssetPath}");
            }
            else
            {
                Selection.activeObject = channel;
                Debug.Log($"ActionChannel already exists at {AssetPath}");
            }
        }
    }
}
#endif