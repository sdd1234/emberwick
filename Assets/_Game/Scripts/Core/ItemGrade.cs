using UnityEngine;

namespace Capstone.Core
{
    /// <summary>기획서 6.2 - 정렬 순서: 전설 > 유니크 > 레어 > 일반</summary>
    public enum ItemGrade
    {
        Common = 0,     // 일반
        Rare = 1,       // 레어
        Unique = 2,     // 유니크
        Legendary = 3   // 전설
    }

    public static class ItemGradeUtil
    {
        public static Color ToColor(this ItemGrade grade) => grade switch
        {
            ItemGrade.Legendary => new Color(0.95f, 0.70f, 0.20f),
            ItemGrade.Unique    => new Color(0.75f, 0.35f, 0.85f),
            ItemGrade.Rare      => new Color(0.30f, 0.60f, 0.95f),
            _                   => new Color(0.75f, 0.75f, 0.75f),
        };

        public static string ToKorean(this ItemGrade grade) => grade switch
        {
            ItemGrade.Legendary => "전설",
            ItemGrade.Unique    => "유니크",
            ItemGrade.Rare      => "레어",
            _                   => "일반",
        };
    }
}
