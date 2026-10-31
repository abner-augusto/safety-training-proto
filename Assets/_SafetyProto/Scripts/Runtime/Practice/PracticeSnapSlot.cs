using UnityEngine;

namespace SafetyProto.Runtime.Practice
{
    /// <summary>
    /// A body slot of the practice area: takes the practice cube the way a PPE slot takes a PPE
    /// item, with none of a PPE slot's reporting (worn PPE, task actions, order checks). Its ghost
    /// shows where the cube goes and turns green while the cube is in reach.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PracticeSnapSlot : MonoBehaviour
    {
        [Tooltip("Interaction id reported when the cube snaps here.")]
        [SerializeField] private string interactionId;
        [SerializeField] private Renderer ghostRenderer;
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color hoverColor = new Color(0.2f, 0.8f, 0.2f, 0.5f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;
        private int _hovering;

        public PracticeSnapItem Item { get; private set; }
        public bool IsOccupied => Item != null;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            _block = new MaterialPropertyBlock();
            Refresh();
        }

        private void OnDisable()
        {
            _hovering = 0;
            Refresh();
        }

        public void OnItemEntered()
        {
            _hovering++;
            Refresh();
        }

        public void OnItemExited()
        {
            _hovering = Mathf.Max(0, _hovering - 1);
            Refresh();
        }

        public void Accept(PracticeSnapItem item)
        {
            Item = item;
            _hovering = 0;
            Refresh();
            if (PracticeArea.Active != null) PracticeArea.Active.MarkDone(interactionId);
        }

        public void Release(PracticeSnapItem item)
        {
            if (Item != item) return;
            Item = null;
            Refresh();
        }

        private void Refresh()
        {
            if (ghostRenderer == null) return;
            ghostRenderer.enabled = !IsOccupied;
            _block.SetColor(ColorId, _hovering > 0 ? hoverColor : idleColor);
            ghostRenderer.SetPropertyBlock(_block);
        }
    }
}
