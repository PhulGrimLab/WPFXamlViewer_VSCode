namespace XamlRenderHost;

/// <summary>
/// 호스트의 식별 정보(버전, 프로토콜 번호). 확장(HostClient)이 ping 응답으로 호환 여부를 판단하는 기준이다.
/// 값 타입 성격의 상수 모음이라 Owner/Lifetime 설명은 생략한다.
/// </summary>
public static class HostInfo
{
    /// <summary>호스트 제품 버전. csproj의 Version과 같아야 한다(HostInfoTests가 확인).</summary>
    public const string Version = "0.1.0";

    /// <summary>확장과 주고받는 프로토콜 번호. 메시지 형식이 호환되지 않게 바뀔 때만 올린다.</summary>
    public const int ProtocolVersion = 1;
}
