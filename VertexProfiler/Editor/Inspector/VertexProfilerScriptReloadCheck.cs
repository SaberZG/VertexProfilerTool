using UnityEditor.Callbacks;
namespace VertexProfilerTool
{
    public static class VertexProfilerScriptReloadCheck
    {
        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            VertexProfilerUtil.ForceReloadProfilerModeAfterScriptCompile = true;
        }
    }
}