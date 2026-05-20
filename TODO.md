# TODO

## Folder Compare Performance

### Persistent precise-compare cache

현재 폴더 비교는 1차 메타데이터 비교 후, 같은 크기 파일을 2차에서 64 KB 버퍼로 바이너리 정밀 비교한다.  
이 구조는 반영되어 있지만, 해시/내용 비교 결과를 다음 비교까지 재사용하는 지속 캐시는 아직 없다.

구현 목표:

- 같은 파일 쌍을 반복 비교할 때 이전 정밀 비교 결과를 재사용한다.
- 파일 크기와 수정 시각이 변하지 않은 경우 디스크 전체 읽기를 건너뛴다.
- 큰 폴더 트리에서 재비교, 복사/삭제 후 재비교, 파일 비교창 저장 후 재비교 속도를 줄인다.

캐시 키 후보:

- left full path
- right full path
- left size
- right size
- left last write time UTC ticks
- right last write time UTC ticks

캐시 값 후보:

- compare status: `Identical`, `DateOnly`, `Modified`
- optional content hash or quick signature
- cached timestamp
- app/cache schema version

무효화 규칙:

- 좌우 경로, 파일 크기, 수정 시각 중 하나라도 바뀌면 캐시 미사용
- 캐시 스키마 버전이 바뀌면 전체 무효화
- 파일 접근 오류가 발생하면 해당 항목은 캐시하지 않음

저장 위치 후보:

- `%LOCALAPPDATA%\SMFolCmp\compare-cache.json`
- 또는 파일 수가 커질 경우 SQLite

주의할 점:

- 캐시는 정확도를 해치면 안 된다.
- 캐시 hit일 때도 `DateOnly`와 `Modified` 구분이 현재 동작과 같아야 한다.
- 사용자가 원하면 캐시 삭제 버튼 또는 설정 메뉴가 필요할 수 있다.
- 매우 큰 캐시 파일을 막기 위해 최대 항목 수 또는 오래된 항목 정리 정책이 필요하다.

### Already implemented

- 1차 메타데이터 결과를 먼저 화면에 표시
- 같은 크기 파일을 2차 정밀 비교 후보로 분리
- 2차 정밀 비교 중 Stop/F5 취소
- 2차 취소 시 빠른 비교 결과 유지
- 폴더 상태 단일 패스 집계
