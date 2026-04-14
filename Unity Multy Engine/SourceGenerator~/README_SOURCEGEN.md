# SyncProperty Source Generator

## 빌드 방법
```
cd SourceGenerator~/MyNetEngine.SourceGen
dotnet build -c Release
```
빌드 결과물 DLL을 Unity 프로젝트에 추가:
```
Assets/MyNetEngine/Plugins/MyNetEngine.SourceGen.dll
```

## Unity에서 등록 방법
Assets/MyNetEngine/Plugins/MyNetEngine.SourceGen.dll 선택 후 Inspector:
- RoslynAnalyzer label 추가

## 사용 방법
```csharp
using MyNetEngine.Objects;

public partial class PlayerNet : NetBehaviour  // partial 필수
{
    [SyncProperty(Priority = 5)] private int hp;
    [SyncProperty] private float posX;

    // 아래가 자동 생성됨:
    // Sync_hp, Sync_posX 프로퍼티 (setter에서 dirty bit 자동 set)
    // Serialize / Deserialize / IsDirty / ClearDirty override
}
```
Sync_XXX 프로퍼티로 값을 쓰면 자동으로 dirty가 서버→클라 전송됨.
직접 필드에 쓰면 dirty 설정 안 됨 (의도적인 설계).
