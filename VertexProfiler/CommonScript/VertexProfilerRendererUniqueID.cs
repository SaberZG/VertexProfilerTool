using System;
using System.Collections.Generic;
using UnityEngine;

namespace VertexProfilerTool
{
    public class VertexProfilerRendererUniqueID : MonoBehaviour
    {
        public static Dictionary<string, Renderer> rendererDict = new Dictionary<string, Renderer>();
        [HideInInspector]
        public string id;
        [HideInInspector]
        public Renderer renderer;
        private void Awake()
        {
            TryInitId();
        }

        public void TryInitId()
        {
            renderer = GetComponent<Renderer>();
            if (string.IsNullOrEmpty(id) && renderer != null)
            {
                id = Guid.NewGuid().ToString();
                rendererDict.TryAdd(id, renderer);
            }
        }

        public static Renderer GetRendererByUniqueId(string id)
        {
            return rendererDict.ContainsKey(id) ? rendererDict[id] : null;
        }
    }
}