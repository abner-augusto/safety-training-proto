using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class FadeHandOverlayTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private Camera _world;
        private Camera _overlay;
        private OVRScreenFade _fade;
        private FadeHandOverlay _router;
        private RenderTexture _target;
        private RenderTexture _previousTarget;
        private Texture2D _pixels;
        private int _handMask;
        private int _uiMask;

        [SetUp]
        public void SetUp()
        {
            _previousTarget = RenderTexture.active;
            _handMask = LayerMask.GetMask("Hands");
            _uiMask = LayerMask.GetMask("PopupUI");
            Assert.That(_handMask, Is.Not.Zero);
            _target = Track(new RenderTexture(32, 32, 24));
            _target.Create();
            _pixels = Track(new Texture2D(32, 32, TextureFormat.RGB24, false));
            _world = CreateCamera("World", CameraClearFlags.SolidColor, 1 | _handMask);
            _overlay = CreateCamera("Overlay", CameraClearFlags.Depth, _handMask | _uiMask);
            _fade = _world.gameObject.AddComponent<OVRScreenFade>();
            _fade.fadeOnStart = false;
            _router = _overlay.gameObject.AddComponent<FadeHandOverlay>();
            SetField("screenFade", _fade);
            SetField("handLayers", (LayerMask)_handMask);
            typeof(FadeHandOverlay).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_router, null);
            CreateCube(2f, 0, Color.blue);
            CreateCube(3f, LayerMask.NameToLayer("Hands"), Color.red);
        }

        [TearDown]
        public void TearDown()
        {
            RenderTexture.active = _previousTarget;
            for (int i = _objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [TestCase(0f, false)]
        [TestCase(0.5f, true)]
        [TestCase(1f, true)]
        public void HandBehindOpaqueObject_IsOverlaidOnlyDuringFade(float alpha, bool expectHand)
        {
            _fade.SetExplicitFade(alpha);
            UpdateMask();
            AssertPixel(expectHand);
            Assert.That(_overlay.cullingMask & _uiMask, Is.EqualTo(_uiMask));
        }

        [Test]
        public void FadeEnds_RestoresWorldOcclusion()
        {
            _fade.SetExplicitFade(1f);
            UpdateMask();
            AssertPixel(true);
            _fade.SetExplicitFade(0f);
            UpdateMask();
            AssertPixel(false);
        }

        [Test]
        public void DisabledFade_RestoresWorldOcclusion()
        {
            _fade.SetExplicitFade(1f);
            UpdateMask();
            _fade.enabled = false;
            UpdateMask();
            AssertPixel(false);
        }

        private void UpdateMask()
        {
            typeof(FadeHandOverlay).GetMethod("OnPreCull", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_router, null);
        }

        private void SetField(string field, object value)
        {
            typeof(FadeHandOverlay).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_router, value);
        }

        private void AssertPixel(bool expectHand)
        {
            _world.Render();
            _overlay.Render();
            RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            _pixels.Apply();
            Color pixel = _pixels.GetPixel(16, 16);
            Assert.That(expectHand ? pixel.r : pixel.b, Is.GreaterThan(0.8f), pixel.ToString());
            Assert.That(expectHand ? pixel.b : pixel.r, Is.LessThan(0.2f), pixel.ToString());
        }

        private Camera CreateCamera(string name, CameraClearFlags clear, int mask)
        {
            var camera = Track(new GameObject(name)).AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(100, 100, 100);
            camera.clearFlags = clear;
            camera.cullingMask = mask;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10;
            camera.useOcclusionCulling = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = _target;
            return camera;
        }

        private void CreateCube(float distance, int layer, Color color)
        {
            var cube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.transform.position = new Vector3(100, 100, 100 + distance);
            cube.layer = layer;
            var material = Track(new Material(Shader.Find("Unlit/Color")));
            material.color = color;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }
    }
}
