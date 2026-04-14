#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MyNetEngine.Core.Metrics;

namespace MyNetEngine.EditorTools
{
    /// <summary>
    /// 간단한 Bandwidth/Profiler 창. per object/rpc 집계 표시.
    /// </summary>
    public sealed class BandwidthProfilerWindow : EditorWindow
    {
        [MenuItem("MyNetEngine/Bandwidth Profiler")]
        public static void Open() => GetWindow<BandwidthProfilerWindow>("Net Bandwidth");

        private Vector2 _scroll;

        private void OnGUI()
        {
            var m = NetMetrics.Global;
            EditorGUILayout.LabelField("Sent bytes", m.TotalBytesSent.ToString());
            EditorGUILayout.LabelField("Recv bytes", m.TotalBytesReceived.ToString());
            EditorGUILayout.LabelField("Packets sent", m.TotalPacketsSent.ToString());
            EditorGUILayout.LabelField("Packets recv", m.TotalPacketsReceived.ToString());
            EditorGUILayout.LabelField("Snapshots", m.SnapshotsBuilt.ToString());
            EditorGUILayout.LabelField("Snapshot bytes", m.SnapshotBytes.ToString());
            EditorGUILayout.LabelField("RPCs sent", m.RpcsSent.ToString());
            EditorGUILayout.LabelField("RPCs recv", m.RpcsReceived.ToString());

            if (GUILayout.Button("Reset")) m.Reset();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-object bytes", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var kv in m.PerObject)
                EditorGUILayout.LabelField($"netId {kv.Key}", kv.Value.ToString());
            EditorGUILayout.LabelField("Per-RPC bytes", EditorStyles.boldLabel);
            foreach (var kv in m.PerRpc)
                EditorGUILayout.LabelField(kv.Key, kv.Value.ToString());
            EditorGUILayout.EndScrollView();
        }

        private void OnInspectorUpdate() => Repaint();
    }
}
#endif
