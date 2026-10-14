# Geuneda Resource

원격 리소스(테이블 같은 데이터 파일) 캐시입니다. 매니페스트 항목(카테고리, 버전, 크기, SHA-256, URL)을 받아 캐시 파일이 맞는지 크기와 해시로 보고, 맞지 않는 것만 받아 검증한 뒤 원자적으로 저장합니다. 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 캐시 | `IResourceCache`, `ResourceCache`, `ResourceCacheOptions` | `Geuneda.Resource` |
| 항목·결과 | `ResourceEntry`, `ResourceCheck`, `ResourceDownloadProgress` | `Geuneda.Resource` |
| 코드 | `ResourceResultCodes`(5000 블록) | `Geuneda.Resource` |

## 설치

`Packages/manifest.json` 에 추가합니다. UPM 은 git 의존성을 자동으로 받지 않으므로 core, http, UniTask 도 함께 넣습니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.http": "https://github.com/geuneda/geuneda-http.git",
    "com.geuneda.resource": "https://github.com/geuneda/geuneda-resource.git"
  }
}
```

## 사용법

### 부트스트랩

```csharp
using Geuneda.Http;
using Geuneda.Resource;

var resources = new ResourceCache(new ResourceCacheOptions
{
    // 일시 실패(연결, 제한 시간, 5xx) 재시도는 RetryMiddleware 에 맡긴다(GET 이라 재시도해도 됨)
    HttpClient = new HttpClientBuilder().AddMiddleware(new RetryMiddleware()).Build(),
    // RootPath 기본 Application.persistentDataPath/geuneda-resources, MaxConcurrentDownloads 기본 2, RequestTimeout 기본 60초
});
MainInstaller.Bind<IResourceCache>(resources);
```

### 매니페스트에서 항목 만들기

항목의 크기·해시로 받은 파일을 검증하므로 항목은 믿을 수 있는 곳에서 가져옵니다. [com.geuneda.appsetting](https://github.com/geuneda/geuneda-appsetting) 의 서명된 문서에 `resources` 노드를 두면 서명이 크기·해시를 보장합니다. 두 패키지는 서로 의존하지 않고 게임이 옮겨 줍니다.

```csharp
var entries = setting.Resources   // AppSetting.Resources (카테고리 → AppSettingResource)
    .Select(pair => new ResourceEntry(pair.Key, pair.Value.Version, pair.Value.Size, pair.Value.Sha256, pair.Value.Url))
    .ToList();
```

### 받기와 읽기

```csharp
var check = await resources.CheckAsync(entries, token);
if (!check.IsSuccess) { ShowRetry(check.Code); return; }

if (!check.Value.IsUpToDate)
{
    // 셀룰러에서는 받기 전에 용량을 묻는다
    if (!await ConfirmDownload(check.Value.TotalBytes)) return;

    var downloaded = await resources.DownloadAsync(check.Value.Missing,
        progress => ShowProgress(progress.CompletedBytes, progress.TotalBytes), token);
    if (!downloaded.IsSuccess) { ShowRetry(downloaded.Code); return; }
}

var tables = entries.First(entry => entry.Category == "tables");
var loaded = await resources.LoadBytesAsync(tables, token);
if (!loaded.TryGetValue(out var data)) { ShowRetry(loaded.Code); return; }

// gamedata 의 설정 형식이면: 받는 쪽은 허용할 설정 타입을 먼저 등록한다(TrustedOnly 바인더)
var serializer = new ConfigsSerializer();
serializer.RegisterAllowedTypes(new[] { typeof(UnitConfig), typeof(StageConfig) });   // 게임의 설정 타입 목록
var configs = serializer.Deserialize<ConfigsProvider>(Encoding.UTF8.GetString(data));
```

- 테이블은 gamedata 의 `ConfigsSerializer` JSON 처럼 이미 구조화된 형식으로 올리면 파싱 결과를 따로 캐시할 필요가 작습니다(레퍼런스의 ptb 2차 캐시는 CSV 파싱이 느려서 둔 것).
- `GetFilePathAsync` 는 파일로 여는 API 용 경로입니다. 다음 다운로드가 새 버전을 저장하면 그 파일은 지워질 수 있습니다.
- 메인 스레드에서 부릅니다. 파일 입출력과 해시는 스레드 풀에서 하고, 결과와 진행률 콜백은 메인 스레드로 돌아옵니다.

### 규칙

- **판정은 크기와 SHA-256**: 버전을 비교하지 않고 캐시 파일(`<루트>/<카테고리>/<버전>.<SHA-256 앞 16자>.bin`)이 항목의 크기·해시와 같은지 봅니다. 이 실행에서 해시까지 확인한 파일은 다시 확인할 때 해시를 건너뜁니다(파일이 있는지는 봄). `LoadBytesAsync` 는 읽은 바이트를 매번 확인합니다.
- **조회는 파일을 바꾸지 않음**: `CheckAsync`·`LoadBytesAsync`·`GetFilePathAsync` 는 맞지 않는 파일을 지우지 않습니다(그 사이 저장된 새 파일을 지우지 않게). 파일을 지우는 것은 저장뿐입니다. 저장할 때 같은 이름 파일이 있으면 다시 확인해, 판정 때 읽지 못했을 뿐인 정상 파일이면 덮어쓰지 않고 그대로 씁니다(다시 확인하다 입출력 오류가 나면 그 파일을 두고 `StorageFailed`).
- **검증 후 원자적 저장**: 받은 바이트의 크기·해시를 먼저 확인하고, 같은 폴더의 임시 파일에 써서 디스크로 내보낸 뒤 이름을 바꿉니다. 맞지 않으면 아무것도 쓰지 않습니다(`IntegrityFailed`). 파일 이름에 해시가 들어가 같은 버전의 내용이 바뀌어도 새 파일은 다른 이름이라, 중간에 끊기거나 실패해도 이전 파일이 남습니다.
- **옛 파일은 저장 뒤에 삭제**: 새 파일을 저장한 뒤 같은 카테고리의 다른 파일(다른 버전·내용, 맞지 않는 파일)과 남은 임시 파일을 지웁니다. 지우지 못한 파일(Windows 에서 열린 파일)은 다음 저장 때 다시 지웁니다. 카테고리마다 한 파일만 남습니다.
- **동시 다운로드 제한**: `MaxConcurrentDownloads`(기본 2)개씩 받습니다. 하나가 실패해도 나머지는 계속 받고, 끝난 뒤 첫 실패 코드와 실패한 항목 목록을 돌려줍니다. 취소하면 시작하지 않은 항목은 받지 않습니다.
- `DownloadAsync` 는 한 번에 하나입니다. 진행 중에 다시 부르면 `InProgress` 입니다. 한 호출에 같은 카테고리의 다른 버전을 넣으면 예외입니다.
- 이름 규칙: 카테고리·버전은 `[a-z0-9._-]` 1~64자(`.`, `..`, 끝의 점, `con`·`nul`·`com1` 같은 Windows 장치 이름(뒤에 점이 붙어도) 제외)라 경로를 벗어나지 못하고, 소문자만이라 대소문자를 가리지 않는 파일 시스템에서도 겹치지 않습니다. URL 은 http·https 절대 주소입니다.
- 응답 본문을 메모리에 받으므로 데이터 파일용입니다. 큰 에셋은 Addressables 원격 카탈로그를 씁니다.
- 저장 파일은 암호화하지 않습니다(무결성만). 앱에 넣은 키는 꺼낼 수 있어 암호화는 난독화 수준이고, 테이블은 어차피 메모리에서 읽습니다.

### 결과

| 상황 | 결과 코드 |
|------|-----------|
| 성공 | 0 |
| 연결 실패, 제한 시간, 취소 | `ResultCodes.NetworkError`, `Timeout`, `Cancelled` |
| 다른 다운로드 진행 중 | `ResultCodes.InProgress` |
| 2xx 가 아닌 응답 | `HttpResultCodes.HttpError` |
| 캐시에 맞는 파일 없음(읽기·경로) | `ResourceResultCodes.NotCached`(5001) |
| 받은 파일의 크기·해시가 다름 | `ResourceResultCodes.IntegrityFailed`(5002) |
| 캐시 폴더에 쓰지 못함 | `ResourceResultCodes.StorageFailed`(5003) |
| null 항목, 같은 카테고리의 다른 버전, 잘못된 이름·해시·주소, 필수 설정 없음 | 예외 |

## 요구 사항

- Unity 2021.3 이상
- `com.geuneda.core` 0.2.0, `com.geuneda.http` 0.1.0, `com.cysharp.unitask` 2.5.10

변경 이력은 [CHANGELOG.md](CHANGELOG.md) 를 참고하세요.

## 라이선스

MIT. [LICENSE.md](LICENSE.md) 를 참고하세요.
