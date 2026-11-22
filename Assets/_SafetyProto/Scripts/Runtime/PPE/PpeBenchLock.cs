using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Keeps the hands off every loose PPE item and decoy until it is released, so nothing worn
    /// or tried on before the training starts can complete a task, log a decoy or reach the score.
    /// Gives back only what it took: an item locked by its slot stays locked.
    /// </summary>
    public sealed class PpeBenchLock
    {
        private readonly List<PPESnapItem> _locked = new List<PPESnapItem>();
        private bool _engaged;

        public bool IsEngaged => _engaged;

        /// <summary>Locks the items on the bench. Call after every item's Awake.</summary>
        public void Engage()
        {
            if (_engaged) return;
            _engaged = true;
            foreach (var item in Object.FindObjectsByType<PPESnapItem>(FindObjectsSortMode.None))
            {
                if (item.IsSnapped) continue;
                item.SetGrabEnabled(false);
                _locked.Add(item);
            }
        }

        public void Release()
        {
            if (!_engaged) return;
            _engaged = false;
            foreach (var item in _locked)
                if (item != null) item.SetGrabEnabled(true);
            _locked.Clear();
        }
    }
}
