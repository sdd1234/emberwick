using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.FX
{
    /// <summary>
    /// 게임 중에 코드로 붙이는 Light2D (빛 플라스크 폭발 · 빛 덩이 · 던지는 병).
    /// 에디터에서 붙이면 Reset 이 "모든 정렬 레이어를 비춘다"로 채워 주지만, 플레이 중 AddComponent 는
    /// 그 목록이 비어 있어서 아무것도 안 비춘다 - 화면에 빛이 안 보인다. 여기서 모든 레이어를 채운다.
    /// </summary>
    public static class LightUtil
    {
        private static FieldInfo _layers;

        public static Light2D AddPoint(GameObject go, Color color, float intensity, float radius)
        {
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            AllLayers(l);
            return l;
        }

        public static void AllLayers(Light2D l)
        {
            if (_layers == null) _layers = typeof(Light2D).GetField("m_ApplyToSortingLayers", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_layers == null) return;
            var all = SortingLayer.layers;
            var ids = new int[all.Length];
            for (int i = 0; i < all.Length; i++) ids[i] = all[i].id;
            _layers.SetValue(l, ids);
        }
    }
}
