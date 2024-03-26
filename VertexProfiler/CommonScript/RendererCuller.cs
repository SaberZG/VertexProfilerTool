using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    [System.Serializable]
    public static class RendererCuller
    {
        private static List<RendererComponentData> rendererComponentDataList = new List<RendererComponentData>();
#if UNITY_2020_1_OR_NEWER
        private static Dictionary<int, GraphicsBuffer> cacheMeshGraphicBufferDict = new Dictionary<int, GraphicsBuffer>();
        private static Dictionary<int, GraphicsBuffer> cacheTrianglesGraphicBufferDict = new Dictionary<int, GraphicsBuffer>();
#else
        private static Dictionary<int, ComputeBuffer> cacheMeshComputeBufferDict = new Dictionary<int, ComputeBuffer>();
#endif

        /// <summary>
        /// 由于URP不支持Replace Shader Shading，因此替换渲染需要全局替换掉当前场景内的材质的渲染Shader
        /// 这里缓存的是Renderer的InstanceId的原生材质列表（sharedMaterial）
        /// </summary>
        private static Dictionary<string, List<Material>> cacheNativeRendererMatForURPDict = new Dictionary<string, List<Material>>();

        private static Dictionary<string, string> vertexProfilerOverrideTagDict = new Dictionary<string, string>()
        {
            ["Opaque"] = "Opaque",
            ["Grass"] = "Opaque",
            ["Terrain"] = "Opaque",
            ["TransparentCutout"] = "Cutout",
            ["LeavesCutout"] = "Cutout",
            ["Transparent"] = "Transparent",
        };

        private static Dictionary<string, Dictionary<string, string>> specialSettingDict = new Dictionary<string, Dictionary<string, string>>()
        {
            // .eg
            ["ShaderName"] = new Dictionary<string, string>()
            {
                ["_SrcBlend"] = "One"
            }
        };
        /// <summary>
        /// 由于URP不支持Replace Shader Shading，因此替换渲染需要全局替换掉当前场景内的材质的渲染Shader
        /// 这里缓存的是Renderer的InstanceId的运行时实例化的替换材质列表（replace Material）
        /// </summary>
        private static Dictionary<string, List<Material>> cacheReplaceRendererMatForURPDict = new Dictionary<string, List<Material>>();
        private static bool hasRevertRendererMat = false;

        private static Dictionary<int, List<MaterialPropertyBlock>> cacheMaterialBlockDict =
            new Dictionary<int, List<MaterialPropertyBlock>>();

         public static void ClearData()
        {
#if UNITY_2020_1_OR_NEWER
            foreach (var gb in cacheMeshGraphicBufferDict.Values)
            {
                gb.Dispose();
            }
            cacheMeshGraphicBufferDict.Clear();

            foreach (var gb in cacheTrianglesGraphicBufferDict.Values)
            {
                gb.Dispose();
            }
            cacheTrianglesGraphicBufferDict.Clear();
#else
            foreach (var cb in cacheMeshComputeBufferDict.Values)
            {
                cb.Dispose();
            }
            cacheMeshComputeBufferDict.Clear();
#endif
            cacheNativeRendererMatForURPDict.Clear();
            cacheReplaceRendererMatForURPDict.Clear();
            cacheMaterialBlockDict.Clear();
        }
        /// <summary>
        /// 每次调度，统一收集当前场景内的带mesh组件的对象
        /// </summary>
        /// <returns></returns>
        public static List<RendererComponentData> GetAllRenderers(bool rawGet = false)
        {
            if (rawGet)
            {
                return rendererComponentDataList;
            }
            rendererComponentDataList.Clear();
            
            var mfs = GetAllMeshFilter();
            var smrs = GetAllSkinnedMeshRenderer();

            Renderer _renderer;

            foreach (var v in mfs)
            {
                _renderer = v.GetComponent<Renderer>();
                if(_renderer == null || !_renderer.enabled) continue;

                RendererComponentData data = new RendererComponentData();
                data.renderer = _renderer;
                data.mf = v;
                rendererComponentDataList.Add(data);
            }
            
            foreach (var v in smrs)
            {
                _renderer = v.GetComponent<Renderer>();
                if(_renderer == null || !_renderer.enabled) continue;

                RendererComponentData data = new RendererComponentData();
                data.renderer = _renderer;
                data.smr = v;
                rendererComponentDataList.Add(data);
            }

            return rendererComponentDataList;
        }

        public static MeshFilter[] GetAllMeshFilter()
        {
            return GameObject.FindObjectsOfType<MeshFilter>(false);
        }
        
        public static SkinnedMeshRenderer[] GetAllSkinnedMeshRenderer()
        {
            return GameObject.FindObjectsOfType<SkinnedMeshRenderer>(false);
        }
        
#if UNITY_2020_1_OR_NEWER
        public static GraphicsBuffer GetGraphicBufferByMesh(Mesh mesh)
        {
            int id = mesh.GetInstanceID();
            if (!cacheMeshGraphicBufferDict.ContainsKey(id))
            {
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                GraphicsBuffer meshBuffer = mesh.GetVertexBuffer(0);
                cacheMeshGraphicBufferDict.Add(id, meshBuffer);
            }

            if (!cacheMeshGraphicBufferDict.ContainsKey(id))
            {
                return null;
            }

            return cacheMeshGraphicBufferDict[id];
        }
#else
        public static ComputeBuffer GetComputeBufferByMesh(Mesh mesh)
        {
            int id = mesh.GetInstanceID();
            if (!cacheMeshComputeBufferDict.ContainsKey(id))
            {
                int count = mesh.vertexCount;
                ComputeBuffer meshBuffer = new ComputeBuffer(count, sizeof(float) * 3);
                meshBuffer.SetData(mesh.vertices);
                cacheMeshComputeBufferDict.Add(id, meshBuffer);
            }

            if (!cacheMeshComputeBufferDict.ContainsKey(id))
            {
                return null;
            }

            return cacheMeshComputeBufferDict[id];
        }
#endif
        public static void TryInitMaterialPropertyBlock(Renderer renderer, int id, int vertexCount)
        {
            if (renderer == null) return;
            int rendererId = renderer.GetInstanceID();
            if (!cacheMaterialBlockDict.ContainsKey(rendererId))
            {
                List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
                int k = 0;
                foreach (var mat in renderer.sharedMaterials)
                {
                    // 不再使用替换渲染的方式实现，直接使用预处理+后处理的方式来实现效果了
                    // 因此不再替换材质球
                    // TrySetOverrideTag(mat);
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, k);
                    block.SetInt(VertexProfilerUtil._RendererId, id);
                    block.SetInt(VertexProfilerUtil._VertexCount, vertexCount);
                    renderer.SetPropertyBlock(block, k);
                    blocks.Add(block);
                    k++;
                }
                cacheMaterialBlockDict.Add(rendererId, blocks);
            }
            else
            {
                List<MaterialPropertyBlock> blocks = cacheMaterialBlockDict[rendererId];
                for (int i = 0; i < blocks.Count; i++)
                {
                    MaterialPropertyBlock block = blocks[i];
                    block.SetInt(VertexProfilerUtil._RendererId, id);
                    block.SetInt(VertexProfilerUtil._VertexCount, vertexCount);
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        private static void TrySetOverrideTag(Material mat)
        {
            Shader shader = mat.shader;
            
            //_SrcBlend
            BlendMode srcBlend = BlendMode.One;
            BlendMode dstBlend = BlendMode.Zero;
            TryGetSpecialSettingValue(shader, "_SrcBlend", out string srcBlendStr);
            if (!srcBlendStr.Equals(string.Empty))
            {
                BlendMode.TryParse(srcBlendStr, out srcBlend);
            }
            else if (mat.HasProperty("_SrcBlend"))
            {
                srcBlend = (BlendMode)mat.GetFloat("_SrcBlend");
            }
            else
            {
                Debug.LogWarningFormat("Mat {0} with Shader {1} doesn't have _SrcBlend property", mat.name, shader.name);
            }
            
            //_DstBlend
            TryGetSpecialSettingValue(shader, "_DstBlend", out string dstBlendStr);
            if (!dstBlendStr.Equals(string.Empty))
            {
                BlendMode.TryParse(dstBlendStr, out dstBlend);
            }
            else if (mat.HasProperty("_DstBlend"))
            {
                dstBlend = (BlendMode)mat.GetFloat("_DstBlend");
            }
            else
            {
                Debug.LogWarningFormat("Mat {0} with Shader {1} doesn't have _DstBlend property", mat.name, shader.name);
            }

            string oriRenderTypeTag = mat.GetTag("RenderType", false);
            string renderTypeTag = "Opaque";
            string blendSrcTag = "";
            string blendDstTag = "";
            int zwrite = -1;
            CullMode cullMode = CullMode.Back;

            if (string.IsNullOrEmpty(oriRenderTypeTag))
            {
                Debug.LogWarningFormat("Mat {0} doesn't have a RenderType tag", mat.name);
            }
            else if (vertexProfilerOverrideTagDict.ContainsKey(oriRenderTypeTag))
            {
                renderTypeTag = vertexProfilerOverrideTagDict[oriRenderTypeTag];
            }
            else // todo 用渲染队列来确定renderTypeTag
            {
                
            }

            if (!renderTypeTag.Equals("Opaque") && !renderTypeTag.Equals("Cutout")) // 
            {
                blendSrcTag = srcBlend.ToString();
                blendDstTag = dstBlend.ToString();
            }
            
            //_ZWrite
            TryGetSpecialSettingValue(shader, "_ZWrite", out string zwriteStr);
            if (!zwriteStr.Equals(string.Empty))
            {
                zwrite = zwriteStr.Equals("On") ? 1 : 0;
            }
            else if (mat.HasProperty("_ZWrite"))
            {
                zwrite = (int)mat.GetFloat("_ZWrite");
            }
            
            //_Cull
            TryGetSpecialSettingValue(shader, "_Cull", out string cullStr);
            if (!cullStr.Equals(string.Empty))
            {
                CullMode.TryParse(cullStr, out cullMode);
            }
            else if(mat.HasProperty("_Cull"))
            {
                cullMode = (CullMode)mat.GetFloat("_Cull");
            }

            string overrideTag = VertexProfilerUtil.GetOverrideTagName(renderTypeTag, blendSrcTag, blendDstTag, zwrite, cullMode);
            mat.SetOverrideTag("VertexProfilerTag", overrideTag);
            
            RecordReplaceSubShader(renderTypeTag, blendSrcTag, blendDstTag, zwrite, cullMode);
        }

        private static void TryGetSpecialSettingValue(Shader shader, string key, out string res)
        {
            res = string.Empty;
            if (!specialSettingDict.ContainsKey(shader.name) || !specialSettingDict[shader.name].ContainsKey(key)) 
                return;
            res = specialSettingDict[shader.name][key];
        }
        
        private static void RecordReplaceSubShader(string renderTypeTag, string blendSrcTag, string blendDstTag,
            int zwrite, CullMode cullMode)
        {
            VertexProfilerEvent.CallRecordReplaceSubShader(renderTypeTag, blendSrcTag, blendDstTag, zwrite, cullMode);
        }
        
        public static void ClearCacheMaterialPropertyBlock()
        {
            cacheMaterialBlockDict.Clear();
        }
#region Replace Material For URP
        /*
         * (已废弃)
         * 由于URP不支持跟内置管线一样的替换渲染功能，因此需要实现一套在编辑器和运行阶段都可以无缝衔接的材质替换功能
         * 不能使用Object.GetInstanceID()函数获取id，此ID并不唯一，在运行和编辑状态切换时会变化
         * 可能还有一些其他的bug，只能发现一个解决一个，增加错题本
         */
        public static string TryGetRendererUniqueId(Renderer renderer)
        {
            if (renderer == null) return string.Empty;

            VertexProfilerRendererUniqueID uniqueID = renderer.GetComponent<VertexProfilerRendererUniqueID>();
            if (uniqueID == null)
            {
                uniqueID = renderer.gameObject.AddComponent<VertexProfilerRendererUniqueID>();
                uniqueID.TryInitId();
            }

            return uniqueID.id;
        }
        /// <summary>
        /// 替换列表内的renderer的渲染材质 多shader （已废弃）
        /// </summary>
        /// <param name="rendererList"></param>
        /// <param name="rpShaderList"></param>
        public static void SetReplaceShader(List<RendererComponentData> rendererList, List<Shader> rpShaderList)
        {
            if (rendererList == null || rendererList.Count <= 0 || rpShaderList == null || rpShaderList.Count <= 0) return;
            for (int i = 0; i < rendererList.Count; i++)
            {
                RendererComponentData data = rendererList[i];
                SetReplaceShader(data.renderer, rpShaderList);
            }
            // 无论是否是重置过材质，在调度替换渲染时都需置为false
            hasRevertRendererMat = false;
        }

        private static bool SetReplaceShader(Renderer renderer, List<Shader> rpShaderList)
        {
            if (renderer == null) return false;

            string rendererId = TryGetRendererUniqueId(renderer);
            // 如果替换材质列表是空的，则需要根据当前的renderer拿到一份材质拷贝
            List<Material> mats = null;
            if (cacheReplaceRendererMatForURPDict.ContainsKey(rendererId))
            {
                mats = cacheReplaceRendererMatForURPDict[rendererId];
            }

            if (mats == null || mats.Count == 0)
            {
                var sharedMats = renderer.sharedMaterials;
                mats = new List<Material>();
                for (int k = 0; k < sharedMats.Length; k++)
                {
                    mats.Add(new Material(sharedMats[k]));
                }
            }

            if (mats.Count == 0) return false;
            bool matHasNull = false;
            foreach (var mat in mats)
            {
                matHasNull |= mat == null;
            }
            if (matHasNull) return false;
            
            bool needResetMaterial = hasRevertRendererMat;
            if (NeedReplaceMaterials(mats, rpShaderList))
            {
                needResetMaterial = true;
                // 检查这批材质的shader是否与将要替换的shader相同
                for (int i = 0; i < rpShaderList.Count; i++)
                {
                    Shader rpShader = rpShaderList[i];
                    int matMaxIndex = mats.Count - 1;
                    Material mat;
                    if (matMaxIndex >= i && mats[i] != null)
                    {
                        mat = mats[i];
                        mat.shader = rpShader;
                    }
                    else
                    {
                        mat = new Material(rpShader);
                        mat.CopyPropertiesFromMaterial(mats[matMaxIndex]);
                        mats.Add(mat);
                    }
                }
                if (rpShaderList.Count < mats.Count)
                {
                    int deleteStart = rpShaderList.Count - 1;
                    mats.RemoveRange(deleteStart, mats.Count - deleteStart);
                }
            }

            if (needResetMaterial)
            {
                // 如果没有缓存过原生材质列表，则在替换之前缓存一次
                if (!cacheNativeRendererMatForURPDict.ContainsKey(rendererId))
                {
                    List<Material> nativeMaterial = renderer.sharedMaterials.ToList();
                    cacheNativeRendererMatForURPDict.Add(rendererId, nativeMaterial);
                }
                renderer.materials = mats.ToArray();
            }
            
            return true;
        }

        static Dictionary<int, bool> tempRecordDic = new Dictionary<int, bool>();
        private static bool NeedReplaceMaterials(List<Material> matList, List<Shader> rpShaderList)
        {
            if (matList.Count != rpShaderList.Count) return true;
            tempRecordDic.Clear();
            bool needReplace = false;
            for (int i = 0; i < matList.Count; i++)
            {
                var mat = matList[i];
                if (mat == null || mat.shader == null || !rpShaderList.Contains(mat.shader) || tempRecordDic.ContainsKey(mat.shader.GetInstanceID()))
                {
                    needReplace = true;
                    break;
                }
                tempRecordDic.Add(mat.shader.GetInstanceID(), true);
            }

            return needReplace;
        }
        
        /// <summary>
        /// 替换列表内的renderer的渲染材质 单shader
        /// </summary>
        /// <param name="rendererList"></param>
        /// <param name="rpShader"></param>
        public static void SetReplaceShader(List<RendererComponentData> rendererList, Shader rpShader)
        {
            if (rendererList == null || rendererList.Count <= 0 || rpShader == null) return;
            for (int i = 0; i < rendererList.Count; i++)
            {
                RendererComponentData data = rendererList[i];
                SetReplaceShader(data.renderer, rpShader);
            }

            // 无论是否是重置过材质，在调度替换渲染时都需置为false
            hasRevertRendererMat = false;
        }
        private static bool SetReplaceShader(Renderer renderer, Shader rpShader)
        {
            if (renderer == null) return false;
            
            string rendererId = TryGetRendererUniqueId(renderer);
            // 如果替换材质列表是空的，则需要根据当前的renderer拿到一份材质拷贝
            List<Material> mats = null;
            if (cacheReplaceRendererMatForURPDict.ContainsKey(rendererId))
            {
                mats = cacheReplaceRendererMatForURPDict[rendererId];
            }

            if (mats == null || mats.Count == 0)
            {
                var sharedMats = renderer.sharedMaterials;
                mats = new List<Material>();
                for (int k = 0; k < sharedMats.Length; k++)
                {
                    mats.Add(new Material(sharedMats[k]));
                }
            }

            if (mats.Count == 0) return false;
            bool matHasNull = false;
            foreach (var mat in mats)
            {
                matHasNull |= mat == null;
            }
            if (matHasNull) return false;
            
            int rpShaderId = rpShader.GetInstanceID();
            
            bool needResetMaterial = hasRevertRendererMat;
            // 检查这批材质的shader是否与将要替换的shader相同
            for (int i = 0; i < mats.Count; i++)
            {
                Material mat = mats[i];
                Shader shader = mat.shader;
                int shaderId = shader.GetInstanceID();
                if (shaderId != rpShaderId)
                {
                    mat.shader = rpShader;
                    needResetMaterial = true;
                }
            }
            
            if (needResetMaterial)
            {
                // 如果没有缓存过原生材质列表，则在替换之前缓存一次
                if (!cacheNativeRendererMatForURPDict.ContainsKey(rendererId))
                {
                    List<Material> nativeMaterial = renderer.sharedMaterials.ToList();
                    cacheNativeRendererMatForURPDict.Add(rendererId, nativeMaterial);
                }
                renderer.materials = mats.ToArray();
            }
            return true;
        }

        // 回滚替换材质
        public static void RevertAllReplaceShader(List<RendererComponentData> rendererList)
        {
            if (rendererList == null || rendererList.Count <= 0 || hasRevertRendererMat) return;
            
            for (int i = 0; i < rendererList.Count; i++)
            {
                RendererComponentData data = rendererList[i];
                RevertReplaceShader(data.renderer);
            }
            
            hasRevertRendererMat = true;
        }
        private static void RevertReplaceShader(Renderer renderer)
        {
            if (renderer == null) return;
            string rendererId = TryGetRendererUniqueId(renderer);
            if (cacheNativeRendererMatForURPDict.ContainsKey(rendererId))
            {
                var matList = cacheNativeRendererMatForURPDict[rendererId];
                renderer.materials = matList.ToArray();
            }
        }
#endregion
    }
}

