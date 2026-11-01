namespace Capstone.Combat
{
    /// <summary>
    /// 피해 종류 (피드백 - 감염자는 단순 물리공격에 잘 안 다친다).
    /// 빛으로 만든 연금술 도구(Light)나 속성 피해(Fire), 방어를 뚫을 만큼 센 물리공격으로 공략해야 한다.
    /// </summary>
    public enum DamageType { Physical, Light, Fire }

    /// <summary>맞은 쪽이 피해를 어떻게 받았는가. 타격감 연출이 고른다.</summary>
    public enum HitResult { Normal, Resisted, Pierced, Weakness }
}
