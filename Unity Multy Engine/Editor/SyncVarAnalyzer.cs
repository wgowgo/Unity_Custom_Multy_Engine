#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MyNetEngine.Objects;

namespace MyNetEngine.EditorTools
{
    /// <summary>
    /// NetBehaviour 상속 타입을 스캔하여 SyncProperty 표시된 필드 통계.
    /// 디자인 타임 단순 점검용.
    /// </summary>
    public sealed class SyncVarAnalyzerWindow : EditorWindow
    {
        [MenuItem("MyNetEngine/SyncVar Analyzer")]
        public static void Open() => GetWindow<SyncVarAnalyzerWindow>("SyncVar Analyzer");

        private Vector2 _scroll;

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var types = TypeCache.GetTypesDerivedFrom<NetBehaviour>();
            foreach (var t in types)
            {
                int count = 0;
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.GetCustomAttribute<SyncPropertyAttribute>() != null) count++;
                }
                EditorGUILayout.LabelField(t.FullName, $"{count} SyncProperty fields");
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
