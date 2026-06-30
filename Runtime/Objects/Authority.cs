namespace MyNetEngine.Objects
{
    /// <summary>
    /// 오브젝트 권한 모델.
    /// 기본은 ServerAuthoritative. owner prediction 허용시 OwnerPredictedServerValidated.
    /// client authoritative는 cosmetic 전용.
    /// </summary>
    public enum AuthorityMode : byte
    {
        ServerAuthoritative = 0,
        OwnerPredictedServerValidated = 1,
        ClientAuthoritativeLimited = 2,
        SharedAuthority = 3
    }
}
