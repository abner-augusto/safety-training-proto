using System;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Re-skins the tracked hand meshes with the glove material while the matching glove is worn,
    /// and restores each hand's original material when it is removed or the session resets.
    /// </summary>
    public class GloveHandSkin : MonoBehaviour, ISessionResettable
    {
        [Serializable]
        private struct HandSkin
        {
            public PPEType glove;
            [Tooltip("The OpenXR hand renderer (LeftHand/RightHand) under OVRHandVisualLeft/Right. " +
                     "The glove texture is baked for the OpenXR hand UV, so the legacy *_handMeshNode renderer will not map it.")]
            public SkinnedMeshRenderer renderer;
        }

        [SerializeField] private Material gloveMaterial;
        [SerializeField] private HandSkin[] hands = Array.Empty<HandSkin>();

        private Material[] _originalMaterials;
        private UnityAction<PPEStateChangedEventArgs> _onPpeStateChanged;

        private void Awake()
        {
            _originalMaterials = new Material[hands.Length];
            for (int i = 0; i < hands.Length; i++)
                _originalMaterials[i] = hands[i].renderer != null ? hands[i].renderer.sharedMaterial : null;
        }

        private void Start()
        {
            if (gloveMaterial == null)
            {
                SafetyLog.Error("GloveHandSkin: glove material not assigned.", this);
                enabled = false;
                return;
            }

            if (!this.IsEventBusReady())
                return;

            _onPpeStateChanged = OnPpeStateChanged;
            EventBus.Instance.onPpeStateChanged.AddListener(_onPpeStateChanged);
        }

        private void OnDestroy()
        {
            if (_onPpeStateChanged != null && EventBus.Instance != null)
                EventBus.Instance.onPpeStateChanged.RemoveListener(_onPpeStateChanged);
        }

        private void OnPpeStateChanged(PPEStateChangedEventArgs args)
        {
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].glove != args.PpeType || hands[i].renderer == null)
                    continue;

                hands[i].renderer.sharedMaterial = args.IsWearing ? gloveMaterial : _originalMaterials[i];
                SafetyLog.Info($"GloveHandSkin: {args.PpeType} {(args.IsWearing ? "applied" : "removed")} on {hands[i].renderer.name}.", this);
            }
        }

        public void ResetSession()
        {
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].renderer != null)
                    hands[i].renderer.sharedMaterial = _originalMaterials[i];
            }
        }
    }
}
