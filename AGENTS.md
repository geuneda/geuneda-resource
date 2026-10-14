# Geuneda.Resource - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.resource`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(Result, ModuleLogger), `com.geuneda.http`(파일 요청), `com.cysharp.unitask`. appsetting 에 의존하지 않습니다(사용자 결정 2026-10-14: 매니페스트는 게임이 `ResourceEntry` 로 옮겨 줌)
- **출처**: Lucky Defense 의 Perbase Resource(로드맵 3-6). 매니페스트 항목으로 캐시 파일의 크기+해시를 판정(버전 비교 없음), 파일 이름에 버전, 받기 전 확인(용량), 진행률은 따랐습니다. 보완한 것: 검증한 뒤에만 저장(레퍼런스는 최종 경로에 쓴 뒤 서명 검증), 임시 파일 후 이름 바꾸기(레퍼런스는 카테고리 폴더를 먼저 통째로 지우고 바로 씀), 옛 버전은 새 버전 저장 뒤 삭제, 동시 다운로드 수 제한(레퍼런스는 없음), 재시도(http `RetryMiddleware`), 파일 입출력·해시를 스레드 풀로. 뺀 것(사용자 결정): 파일 단위 서명(매니페스트 서명이 해시를 보장), 저장 암호화(XOR+AES), 파싱 결과 2차 캐시(ptb).

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/ResourceCache.cs` | 판정·받기·읽기, 동시 다운로드 워커, 이 실행의 확인 기록(`_verified`, 카테고리 → 항목) |
| `Runtime/ResourceFiles.cs` | 경로, 크기·해시 확인, 원자적 저장(`Commit`), 옛 버전·임시 파일 정리. 스레드 풀에서 불려 Unity API 를 쓰지 않음 |
| `Runtime/ResourceEntry.cs` | 항목과 이름·해시·주소 규칙 |
| `Runtime/IResourceWorker.cs`, `ThreadPoolResourceWorker.cs` | 파일 작업을 돌리는 곳. 기본은 `UniTask.RunOnThreadPool`(메인 스레드로 돌아옴), 테스트는 `InlineResourceWorker` |
| `Runtime/ResourceCheck.cs`, `ResourceDownloadProgress.cs`, `ResourceResultCodes.cs`, `IResourceCache.cs`, `ResourceCacheOptions.cs` | 결과·설정 |

## 3. 규칙

- **결과 규약**: 정상 실패(네트워크, 응답, 무결성, 저장, 진행 중)는 `Result` 코드, 오용(null 항목, 같은 카테고리 두 버전, 잘못된 이름·해시·주소, 설정)은 예외입니다. 공개 메서드는 async 가 아닌 겉 메서드에서 인자를 검사해 바로 던지고 실제 작업은 private async 메서드가 합니다.
- **이름**: 카테고리·버전은 `[a-z0-9._-]` 1~64자, `.`·`..` 제외. 파일 경로(`<루트>/<카테고리>/<버전>.<SHA-256 앞 16자>.bin`)가 되므로 이 규칙을 느슨하게 하지 마세요. 소문자만 받는 것은 대소문자를 가리지 않는 파일 시스템(Windows, macOS 기본)에서 `Tables`·`tables` 가 같은 폴더가 되어 정리가 다른 카테고리 파일을 지우지 않게 하려는 것이고, 끝의 점과 Windows 장치 이름(`con`, `prn`, `aux`, `nul`, `com0~9`, `lpt0~9`, 뒤에 점이 붙어도)은 Windows 에서 저장할 수 없거나 다른 이름이 되어서 막습니다(2026-10-14 리뷰). appsetting 의 `AppSettingResource` 도 같은 규칙입니다(두 곳을 함께 바꿈).
- **조회는 비파괴**: `CheckAsync`·`LoadBytesAsync`·`GetFilePathAsync` 는 파일을 지우지 않고 확인 기록만 지웁니다(`Forget`). 조회가 지우면 판정과 삭제 사이에 저장된 새 파일을 지운다(2026-10-14 리뷰). 파일을 지우는 곳은 `ResourceFiles.Commit` 하나입니다.
- **저장 순서**: 크기·해시 확인 → 같은 폴더 `.<버전>.<guid>.tmp` 에 쓰고 `Flush(true)` → 같은 이름 파일이 있으면 다시 확인해 정상이면 쓰지 않고 그대로 씀(판정 때 입출력 오류로 읽지 못하면 받는 대상이 되므로 "받는다 = 깨졌다"가 아님, 2026-10-14 리뷰), 다시 확인이 입출력 오류면 예외로 그 파일을 둔 채 `StorageFailed`, 맞지 않으면 지움 → `File.Move` → 다른 `*.bin`·`.*.tmp` 삭제. 파일 이름에 해시 앞 16자를 넣어 같은 버전의 내용 교체도 다른 이름이 되므로 이전 파일은 새 파일이 놓인 뒤에야 지워집니다. `File.Move(src, dst, overwrite)` 는 netstandard2.1·Mono 에서 보장되지 않아 쓰지 않습니다. 정리 대상은 경로 문자열이 아니라 파일 이름으로 비교합니다(구분자 형태가 달라 새 파일을 지우지 않게).
- **확인 기록**: `_verified` 는 카테고리마다 마지막으로 확인한 항목입니다. 같은 파일(카테고리·버전·크기·해시)이면 `CheckAsync`·`GetFilePathAsync` 는 해시 없이 파일 존재만 봅니다. 새 실행은 해시를 다시 계산합니다. `LoadBytesAsync` 는 읽은 바이트를 항상 확인합니다.
- **동시성**: 모든 공개 메서드는 메인 스레드에서 부르고, 워커는 메인 스레드에서 `next` 인덱스를 나눠 씁니다(파일 작업만 스레드 풀). `DownloadAsync` 는 `_downloading` 하나로 겹치지 않게 하고 겹치면 기다리지 않고 `InProgress` 입니다. 파일을 바꾸는 것은 다운로드 저장뿐이고 다운로드는 하나라 카테고리 잠금은 두지 않았습니다. 조회가 오래된 항목으로 확인 기록을 남겨도 다음 확인에서 파일 존재·항목 비교로 걸러집니다.
- **워커 예외**: 워커는 예외를 잡아 두고 연결된 토큰으로 다른 워커를 멈춘 뒤(취소 콜백이 던지는 예외는 로그만 남기고 워커 밖으로 내보내지 않음), 모든 워커가 끝나면 `ExceptionDispatchInfo` 로 다시 던집니다. `UniTask.WhenAll` 은 첫 예외에서 바로 끝나 `_downloading` 이 남은 워커보다 먼저 풀리기 때문입니다(2026-10-14 리뷰).
- **취소**: 작업에 들어갈 때, 파일 작업을 기다린 뒤, 결과를 돌려주기 전에 확인합니다(빈 목록·마지막 항목 포함). 시작한 저장은 끝까지 합니다.
- **실패 집계**: 취소가 아닌 실패가 있으면 첫 실패 코드와 실패 목록, 취소만 있었으면 `Cancelled` 입니다.
- 결과 코드는 5000 블록(core README 범위 표에 등록). 값은 바꾸거나 재사용하지 않습니다.
- static 가변 상태 금지(D2). 서비스는 인스턴스로 만들어 `MainInstaller` 에 인터페이스로 바인딩합니다.

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Resource.Tests.EditMode`)
  - `ResourceCacheTest`: `FakeFileServer`(주소별 파일·상태·실패·게이트, 동시 요청 수 기록)와 `InlineResourceWorker`, 임시 폴더로 저장·읽기·경로·임시 파일 없음·진행률·제한 시간, 판정과 건너뛰기, 재시작 뒤 같은 크기 변조 감지·삭제·다시 받기, 새 버전 저장 뒤 옛 버전·남은 임시 파일 삭제, 크기·해시 불일치 시 저장 안 함·옛 버전 유지, HTTP·전송 실패 집계와 나머지 계속, 동시 다운로드 제한, `InProgress`, 취소, 같은 카테고리 두 버전·null, 쓸 수 없는 폴더, 깨진 파일 읽기, 진행률 콜백 예외
  - `ResourceEntryTest`: 이름(소문자, 끝의 점, Windows 장치 이름과 그렇게 보이기만 하는 이름)·크기·해시·주소 규칙
  - 경합·실패 주입: `DeferredResourceWorker`(쌓아 두고 순서 제어)로 조회와 저장의 경합, `FailingResourceWorker`(처음 작업을 입출력 오류로)로 판정 때 못 읽은 정상 파일의 재사용, `FakeFileServer.Throws`·`IgnoreCancellation`·`ThrowOnCancel` 로 워커 예외와 취소 콜백 예외
  - `UnityWebRequestDownloadTest`: 로컬 `HttpListener` 와 실제 `UnityWebRequestTransport`, 실제 스레드 풀로 128KB 받기·읽기·확인, 결과가 호출 스레드로 돌아오는지, 404 뒤에도 캐시 유지
