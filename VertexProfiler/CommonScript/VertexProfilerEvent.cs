using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    public delegate void LogoutToExcel(DisplayType displayType, List<ProfilerDataContents> logoutDataList, RenderTexture screenShot = null, Texture2D screenShotWithGrids = null);
    public delegate void RecordReplaceSubShader(string renderTypeTag, string blendSrcTag, string blendDstTag, int zwrite, CullMode cullMode);
    public delegate void TriggerRegenerateReplaceShader();

    public class VertexProfilerEvent
    {
        public static LogoutToExcel LogoutToExcelEvent;
        public static RecordReplaceSubShader RecordReplaceSubShaderEvent;
        public static TriggerRegenerateReplaceShader TriggerRegenerateReplaceShaderEvent;

        public static void CallLogoutToExcel(DisplayType displayType, List<ProfilerDataContents> logoutDataList,
            RenderTexture screenShot = null, Texture2D screenShotWithGrids = null)
        {
            if (LogoutToExcelEvent != null)
            {
                LogoutToExcelEvent.Invoke(displayType, logoutDataList, screenShot, screenShotWithGrids);
            }
        }

        public static void CallRecordReplaceSubShader(string renderTypeTag, string blendSrcTag, string blendDstTag, int zwrite, CullMode cullMode)
        {
            if (RecordReplaceSubShaderEvent != null)
            {
                RecordReplaceSubShaderEvent.Invoke(renderTypeTag, blendSrcTag, blendDstTag, zwrite, cullMode);
            }
        }

        public static void CallTriggerRegenerateReplaceShader()
        {
            if (TriggerRegenerateReplaceShaderEvent != null)
            {
                TriggerRegenerateReplaceShaderEvent.Invoke();
            }
        }
    }
}