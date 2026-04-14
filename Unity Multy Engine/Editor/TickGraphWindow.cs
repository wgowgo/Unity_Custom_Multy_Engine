#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MyNetEngine.EditorTools
{
    /// <summary>
    /// tick 주기/지터 시각화 창 placeholder.
    /// NetworkRunner reference를 잡고 Tick.CurrentTick / InterpolationAlpha 등을 그래프로.
    /// </summary>
    public sealed class TickGraphWindow : EditorWindow
    {
        [MenuItem("MyNetEngine/Tick Graph")]
        public static void Open() => GetWindow<TickGraphWindow>("Tick Graph");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Hook this window to your NetworkRunner instance to visualize tick / interpolation alpha / time offset.", MessageType.Info);
        }
    }
}
#endif
