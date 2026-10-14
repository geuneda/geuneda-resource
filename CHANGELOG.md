# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-14

**New**:
- 원격 리소스 캐시 `IResourceCache`/`ResourceCache`: 매니페스트 항목 `ResourceEntry`(카테고리, 버전, 크기, SHA-256, URL. 이름은 경로를 벗어나지 않고 대소문자를 가리지 않는 파일 시스템에서도 겹치지 않게 `[a-z0-9._-]` 로 제한, 끝의 점과 Windows 장치 이름 금지)의 캐시 파일이 맞는지 크기·해시로 판정(`CheckAsync`, 받을 항목과 총 크기), 맞지 않는 것만 받기(`DownloadAsync`), 확인한 바이트 읽기(`LoadBytesAsync`), 경로(`GetFilePathAsync`)
- 받은 바이트를 검증한 뒤에만 저장(같은 폴더 임시 파일 → 디스크로 내보내기 → 이름 바꾸기). 파일 이름 `<버전>.<SHA-256 앞 16자>.bin` 이라 같은 버전의 내용 교체도 새 파일이 놓인 뒤에야 이전 파일 삭제. 새 파일 저장 뒤 같은 카테고리의 다른 파일·남은 임시 파일 삭제. 조회는 파일을 지우지 않음(삭제는 저장만), 저장 때 같은 이름의 정상 파일은 덮어쓰지 않고 재사용
- 동시 다운로드 수 제한(기본 2), 하나가 실패해도 나머지 계속, 취소 시 남은 항목 중단, 한 번에 하나(`InProgress`), 진행률 콜백(예외 격리), 워커 예외는 모든 워커가 끝난 뒤 다시 던짐(취소 콜백 예외는 로그), 작업 진입·파일 작업 뒤 취소 확인, 요청 제한 시간(기본 60초), 재시도는 http `RetryMiddleware`
- 파일 입출력과 해시는 스레드 풀, 결과는 메인 스레드
- 결과 코드 `ResourceResultCodes`(5000 블록: `NotCached` 5001, `IntegrityFailed` 5002, `StorageFailed` 5003)
