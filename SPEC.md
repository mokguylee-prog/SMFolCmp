# SMFolCmp Functional Specification

문서 상태: 현재 구현 기준  
목적: 이 문서만으로 동일한 프로그램을 C# 또는 Swift로 다시 구현할 수 있도록, 현재 앱의 동작과 UI를 기능 단위로 명세한다.

## 1. 제품 개요

SMFolCmp는 두 폴더 또는 두 텍스트 파일을 좌우로 비교하는 데스크톱 애플리케이션이다.

현재 구현은 다음 3개 화면으로 구성된다.

1. 폴더 비교창 `MainWindow`
2. 파일 비교창 `CompareWindow`
3. Windows 탐색기 연동 설정창 `SetupWindow`

현재 구현의 주요 목적은 다음과 같다.

- 두 폴더의 파일 구조와 차이를 빠르게 확인한다.
- 파일을 좌우로 복사하거나 삭제한다.
- 텍스트 파일의 줄 단위 및 문자 단위 차이를 확인하고 직접 편집한다.
- Windows 탐색기 우클릭 메뉴에서 파일/폴더 비교를 바로 시작한다.

## 2. 지원 환경과 기술 전제

### 2.1 현재 구현 환경

- OS: Windows
- UI 프레임워크: WPF
- 런타임: .NET 8
- 빌드 대상: `net8.0-windows`
- 배포 형태:
  - `WinExe`
  - `SelfContained = true`
  - `PublishSingleFile = true`
- 외부 패키지:
  - `Ookii.Dialogs.Wpf` 5.0.1

### 2.2 포팅 시 구분해야 할 영역

- 플랫폼 독립 영역:
  - 폴더 비교 로직
  - 파일 비교 로직
  - 상태 모델
  - 편집 동작
  - 필터와 정렬
- Windows 전용 영역:
  - 레지스트리 저장
  - 탐색기 컨텍스트 메뉴 등록
  - `.exe` 경로 기반 쉘 명령

Swift로 재구현할 경우 핵심 기능은 그대로 유지하되, Windows 레지스트리와 탐색기 메뉴는 대상 OS에 맞는 저장소와 Finder 확장 또는 서비스 메뉴로 치환해야 한다.

## 3. 용어

| 용어 | 의미 |
| --- | --- |
| Left | 좌측 비교 대상 |
| Right | 우측 비교 대상 |
| File Item | 폴더 비교창에서 한 행을 나타내는 항목 |
| Diff Line | 파일 비교창에서 한쪽의 한 줄을 나타내는 모델 |
| Placeholder | 반대편 줄 수를 맞추기 위해 시각적으로만 존재하는 빈 줄 |
| Pending Compare | 탐색기에서 선택이 나누어 전달될 때 3초 동안 임시로 첫 항목을 저장하는 상태 |

## 4. 애플리케이션 시작 동작

### 4.1 오류 처리

앱 시작 시 전역 예외 핸들러를 등록한다.

- `AppDomain.CurrentDomain.UnhandledException`
- `DispatcherUnhandledException`

예외 발생 시:

1. 실행 폴더의 `error.log`에 날짜와 전체 예외 문자열을 기록한다.
2. 제목 `Fatal Error`의 메시지 박스를 띄운다.
3. Dispatcher 예외는 `Handled = true` 처리한다.

### 4.2 명령행 진입점

앱은 다음 명령행 형태를 지원한다.

#### A. 일반 실행

```text
SMFolCmp.exe
```

- 저장된 좌우 폴더가 있으면 폴더 비교창을 띄우고 자동 비교한다.
- 저장된 폴더가 없으면 빈 폴더 비교창을 띄운다.

#### B. 왼쪽 대상 저장

```text
SMFolCmp.exe left:"<path>"
```

- `<path>`가 파일이면 `LeftFile` 저장
- `<path>`가 폴더이면 `LeftFolder` 저장
- UI 없이 조용히 종료

#### C. 저장된 왼쪽과 비교

```text
SMFolCmp.exe compare:"<path>"
```

처리 순서:

1. `PendingPath`, `PendingTime`이 존재하고 3초 이내이며 현재 path와 다르면 pending 경로와 즉시 비교한다.
2. pending 조건을 만족하지 않으면:
   - 현재 path가 파일이면 `LeftFile`
   - 현재 path가 폴더이면 `LeftFolder`
   를 조회한다.
3. 저장된 left가 유효하면 즉시 비교창을 연다.
4. 저장된 left도 없으면 현재 path를 `PendingPath`, 현재 tick을 `PendingTime`에 저장하고 조용히 종료한다.

비교창 선택 규칙:

- 파일이 하나라도 포함되면 `CompareWindow`
- 둘 다 폴더이면 `MainWindow`

#### D. 다중 선택 비교

```text
SMFolCmp.exe --compare-selected "<path1>" "<path2>"
```

- 존재하는 path만 추린다.
- 정확히 2개이며 둘 다 파일 또는 둘 다 폴더인 경우:
  - 파일이면 `CompareWindow`
  - 폴더이면 `MainWindow`
- 정확히 1개만 들어온 경우:
  - `HandlePendingCompare()`를 이용해 3초 pending 비교 흐름으로 처리
- 그 외:
  - `Compare requires exactly two folders or exactly two files.` 메시지 표시 후 종료

#### E. 레거시 쌍 비교

```text
SMFolCmp.exe --compare-pair "<path1>" "<path2>"
```

- 둘 다 파일 또는 둘 다 폴더이면 바로 비교창을 연다.
- 현재 UI에서는 직접 쓰지 않지만 코드가 남아 있는 호환 경로다.

## 5. 영속 저장소

현재 구현은 `HKEY_CURRENT_USER\Software\SMFolCmp`를 사용한다.

| 키 | 용도 |
| --- | --- |
| `LeftFolder` | 마지막 왼쪽 폴더 |
| `RightFolder` | 마지막 오른쪽 폴더 |
| `LeftFile` | 탐색기 메뉴에서 저장한 왼쪽 파일 |
| `RightFile` | 파일 비교창 종료 시 빈 문자열로 초기화 |
| `PendingPath` | 다중 선택 전달 보완용 임시 경로 |
| `PendingTime` | `PendingPath` 저장 시각 tick |
| `CompareShowOnlyDiff` | 파일 비교창 필터 상태. `"1"`이면 Diff, `"0"`이면 All |
| `ExcludeFilePatterns` | 폴더 비교창에서 화면 표시에서 제외할 파일명 패턴 문자열 |
| `ExcludeFolderPatterns` | 폴더 비교창에서 화면 표시에서 제외할 폴더명 패턴 문자열 |

참고:

- 폴더 비교창의 `All` / `Diff` / `Diff2` 필터 상태는 현재 저장하지 않는다.
- 폴더 비교창의 파일/폴더 배제 패턴은 저장한다.
- 파일 비교창의 기본 필터는 `Diff`다.

## 6. Windows 탐색기 컨텍스트 메뉴

### 6.1 설정 화면 역할

`SetupWindow`는 다음을 수행한다.

- 실행 파일 경로 입력
- 실행 파일 직접 찾기
- 컨텍스트 메뉴 등록
- 컨텍스트 메뉴 해제
- 현재 등록 상태 표시

세부 규칙:

- 기본 실행 파일 경로는 `AppContext.BaseDirectory` 아래의 `SMFolCmp.exe`
- 등록 상태 문구는 등록됨이면 초록색, 미등록이면 회색
- 아이콘은 실행 파일과 같은 폴더의 `SMFolCmp.ico`가 있으면 그것을 사용하고, 없으면 실행 파일 자체를 사용
- 등록 시 경로가 비어 있거나 파일이 없으면 레지스트리를 수정하지 않고 오류 문구만 표시

### 6.2 등록되는 메뉴

#### 폴더 1개 선택

레지스트리:

```text
HKCU\Software\Classes\Directory\shell\SMFolCmpLeft
HKCU\Software\Classes\Directory\shell\SMFolCmpCompare
```

메뉴:

- `SMFolCmp with Left`
- `SMFolCmp and Compare`

명령:

```text
"<exe>" left:"%1"
"<exe>" compare:"%1"
```

각 키의 `MultiSelectModel`은 `Single`.

#### 파일 1개 선택

레지스트리:

```text
HKCU\Software\Classes\*\shell\SMFolCmpLeft
HKCU\Software\Classes\*\shell\SMFolCmpCompare
```

메뉴와 명령은 폴더 1개 선택과 동일하다.

#### 파일 또는 폴더 2개 선택

레지스트리:

```text
HKCU\Software\Classes\Directory\shell\SMFolCmpMultiCompare
HKCU\Software\Classes\*\shell\SMFolCmpMultiCompare
```

메뉴:

- `-SMFolCmp`

명령:

```text
"<exe>" --compare-selected "%1"
```

각 키의 `MultiSelectModel`은 `Player`.

### 6.3 등록 상태 판정

다음 6개 키가 모두 있어야 등록 상태로 본다.

- 폴더 Left
- 폴더 Compare
- 파일 Left
- 파일 Compare
- 폴더 MultiCompare
- 파일 MultiCompare

### 6.4 등록 해제

현재 키뿐 아니라 과거 시도에서 남을 수 있는 레거시 키도 제거한다.

- `SMFolCmp`
- `SMFolCmpLeft`
- `SMFolCmpCompare`
- `SMFolCmpPairCompare`
- `SMFolCmpMultiCompare`

폴더와 파일 키 모두 제거한다.

## 7. 폴더 비교창

### 7.1 창 기본값

- 창 제목: 실행 중 조립
  - 형식: `SMFolCmp v<version> (<build-date>)`
- 기본 크기: `1400 x 700`
- 시작 위치: 화면 중앙
- 기본 배경: 다크 테마

### 7.2 화면 구조

위에서 아래 순서:

1. 헤더
   - 좌측: `SMFolCmp`
   - 우측: `Setup`
2. 경로 선택 영역
   - Left path box + `Browse Left`
   - 가운데 `Compare` 버튼
   - 가운데 `Swap` 버튼 `⇌`
   - `Browse Right` + Right path box
3. 필터 영역
   - `All`
   - `Diff`
   - `Diff2`
   - `파일배제:` 패턴 입력 + `저장`
   - `폴더배제:` 패턴 입력 + `저장`
4. 컬럼 헤더
   - 좌우 각각:
     - 트리 선
     - 아이콘/마커
     - Name
     - Size
     - Modified
5. 좌우 비교 목록
6. 상태 바

참고:

- 좌우 경로 텍스트박스는 현재 구현에서 사실상 표시용이다.
- 사용자가 텍스트박스에 경로를 직접 입력해도 `_leftFolder`, `_rightFolder` 필드에는 자동 반영되지 않는다.
- 실제 경로 반영은 폴더 선택 버튼, 탐색기/생성자 인수, 또는 경로 영역으로 폴더를 드래그 앤 드롭할 때 일어난다.
- 드래그 앤 드롭은 파일 드롭 형식을 받지만, 실제 반영은 첫 번째 항목이 폴더일 때만 한다.
- 왼쪽/오른쪽 경로 영역은 `AllowDrop = true`이며 `PreviewDragEnter`, `PreviewDragOver`에서 복사 효과를 표시한다.

### 7.2.1 좌우 폴더 Swap

- `⇌` 버튼을 누르면 `_leftFolder`와 `_rightFolder` 값을 서로 교환한다.
- 좌우 경로 텍스트박스도 같이 갱신한다.
- 교환 후 레지스트리에 저장하고 즉시 비교를 다시 시작한다.
- 한쪽이라도 폴더가 비어 있으면 `두 폴더를 모두 선택하세요` 경고를 표시하고 동작하지 않는다.

### 7.3 좌우 목록 행 표현

#### 폴더 행

- 폴더 아이콘:
  - 접힘: `\uE8B7`
  - 펼침: `\uE838`
- 폴더 아이콘 색: RGB `(244, 184, 72)`
- 배경 없음

#### 파일 행

- 파일명 앞에 작은 정사각형 마커
- 크기: `7 x 7`
- 색상: 해당 항목의 텍스트 색상

#### 트리 연결선

- `│`, `├─`, `└─` 문자를 사용
- 실제 보이는 형제 관계 기준으로 연결선 계산
- Diff 필터 상태에서도 마지막 형제 여부가 다시 계산되어 선이 끊기지 않아야 한다.

### 7.4 FileItem 데이터 모델

필수 필드:

- `Name`
- `LeftPath`
- `RightPath`
- `IsDirectory`
- `LeftSize`
- `RightSize`
- `LeftModified`
- `RightModified`
- `Children`
- `IsExpanded`
- `Depth`
- `IsLastVisibleSibling`
- `AncestorHasNextVisibleSibling`

파생값:

- `LeftDisplayName`, `RightDisplayName`
- `LeftSizeText`, `RightSizeText`
- `LeftModifiedText`, `RightModifiedText`
- `StatusText`
- 행/텍스트/아이콘 색상
- `TreeLineString`

표시 포맷:

- 파일 크기
  - `0 ~ 1023`: `<n> B`
  - `1024 ~ 1048575`: 소수점 1자리 `KB`
  - `1048576 ~ 1073741823`: 소수점 1자리 `MB`
  - 그 이상: 소수점 1자리 `GB`
- 수정 시각: `yyyy-MM-dd HH:mm:ss`
- 존재하지 않는 쪽의 파일명은 빈 문자열
- 폴더 행은 한쪽이 실제로 없더라도 현재 구현상 양쪽 크기 칸 모두 `<Folder>`로 표시된다.

### 7.5 비교 상태

| 상태 | 의미 | 행 색상 | 텍스트 색상 |
| --- | --- | --- | --- |
| `Identical` | 내용과 날짜 모두 동일 | `(45,45,48)` | `(224,224,224)` |
| `DateOnly` | 크기와 내용은 동일, 날짜만 다름 | `(112,72,24)` | `(255,190,96)` |
| `Modified` | 파일 내용 다름 | `(120,80,40)` | `(255,100,100)` |
| `LeftOnly` | 왼쪽에만 존재 | 좌 `(60,100,140)`, 우 `(70,70,70)` | `(120,180,255)` |
| `RightOnly` | 오른쪽에만 존재 | 좌 `(70,70,70)`, 우 `(60,120,60)` | `(120,180,255)` |

### 7.6 파일 비교 판정 규칙

두 파일이 모두 존재할 때 비교는 2단계로 진행한다.

#### 1차 빠른 비교

1. 크기가 다르면 즉시 `Modified`
2. 크기가 같고 수정 시각이 다르면 임시로 `DateOnly`
3. 크기와 수정 시각이 같으면 임시로 `Identical`
4. 크기가 같은 파일은 모두 2차 정밀 비교 후보에 넣는다.

#### 2차 정밀 비교

1. 크기가 다르면 `Modified`
2. 크기가 같으면 바이너리 내용을 64 KB 버퍼로 전부 비교
3. 내용이 다르면 `Modified`
4. 내용은 같고 `LastWriteTime`만 다르면 `DateOnly`
5. 내용과 날짜 모두 같으면 `Identical`

주의:

- 1차 결과는 사용자가 빠르게 구조를 볼 수 있도록 먼저 보여주는 임시 결과다.
- 같은 크기 파일은 2차 정밀 비교가 끝날 때까지 `DateOnly` 또는 `Identical`에서 `Modified`로 바뀔 수 있다.
- 현재 구현에는 해시/내용 비교 결과의 지속 캐시가 없다. 같은 크기 파일은 비교를 실행할 때마다 2차에서 다시 바이너리 내용을 확인한다.

폴더 항목 수집 규칙:

- 이름 비교는 `StringComparer.OrdinalIgnoreCase` 기반이라 대소문자만 다른 이름은 같은 항목으로 본다.
- 각 폴더의 파일과 하위 폴더를 모두 수집한 뒤 양쪽 이름의 합집합을 만든다.
- 실제 화면 출력 전에는 폴더 우선, 그 다음 이름 오름차순으로 다시 정렬한다.

### 7.7 폴더 상태 집계 규칙

폴더는 하위 항목을 재귀 비교한 뒤 상태를 정한다.

- 자식 폴더는 이미 자신의 하위 상태를 반영한 상태여야 한다.
- 각 폴더는 직접 자식 목록을 한 번만 훑어 상태를 집계한다.
- 과거처럼 같은 하위 트리를 상태별로 여러 번 재탐색하지 않는다.

우선순위:

1. 하위에 `Modified`가 하나라도 있으면 폴더는 `Modified`
2. 하위에 `LeftOnly`와 `RightOnly`가 모두 있으면 폴더는 `Modified`
3. 하위에 `LeftOnly`만 있으면 `LeftOnly`
4. 하위에 `RightOnly`만 있으면 `RightOnly`
5. 하위에 `DateOnly`만 있으면 `DateOnly`
6. 그 외는 `Identical`

### 7.8 비교 실행

- 대기 중:
  - `Compare` 버튼 또는 `F5`로 비교 시작
- 비교 중:
  - 같은 버튼 또는 `F5`가 중지 요청으로 동작
  - 버튼은 비활성화하지 않음
  - 버튼 텍스트 `■ Stop (F5)`
  - 중지 요청 후 버튼 텍스트 `⏳ Stopping...`
  - 1차 상태 바 `빠른 폴더 비교 중...`
- 1차 빠른 비교가 끝나면:
  - 메타데이터 기준 결과를 즉시 화면에 표시
  - 상태 바 끝에 `정밀 비교 중: <done>/<total>` 표시
- 2차 정밀 비교가 끝나면:
  - 파일 상태를 실제 내용 기준으로 보정
  - 폴더 상태를 다시 집계
  - 화면 목록과 상태 수를 최종 결과로 갱신
- 비교는 취소 가능한 백그라운드 작업으로 실행
- 폴더 열거, 재귀 순회, 파일 내용 비교에서 취소 토큰을 확인
- 파일 내용 비교 중에는 64 KB 청크 단위로 취소 요청을 반영
- 1차 빠른 비교 전에는 새 결과를 임시 트리에 만들고, 빠른 비교가 끝났을 때만 화면 데이터와 교체
- 1차 중지 시에는 기존 결과를 유지하고 상태 바에 `폴더 비교가 중지되었습니다.` 표시
- 2차 중지 시에는 이미 표시된 1차 결과를 유지하고 상태 바에 `정밀 비교가 중지되었습니다. 빠른 비교 결과만 표시 중입니다.` 표시
- 완료 후:
  - 펼쳐져 있던 폴더 상태 복원
  - 플랫 목록 재생성
  - 상태 수 갱신
  - 버튼 텍스트 `⟳ F5`
  - 버튼은 다시 비교 시작 상태로 복귀

### 7.9 정렬과 필터

정렬 순서:

1. 폴더 우선
2. 이름 오름차순

`All`:

- 모든 항목 표시

`Diff`:

- `Identical`이 아닌 항목 표시
- 폴더 자신이 동일이어도 하위에 표시 대상이 있으면 폴더는 표시

`Diff2`:

- `Identical`과 `DateOnly`를 제외한 항목 표시
- 즉 내용 변경, 왼쪽만, 오른쪽만 존재하는 항목만 차이로 본다.
- 폴더 자신이 동일 또는 DateOnly여도 하위에 `Diff2` 표시 대상이 있으면 폴더는 표시

파일/폴더 배제 패턴:

- 패턴 문자열은 세미콜론 `;`으로 구분한다.
- 각 패턴은 앞뒤 공백을 제거하고 빈 패턴은 무시한다.
- 파일 패턴은 파일명에만 적용하고, 폴더 패턴은 폴더명에만 적용한다.
- 지원 와일드카드:
  - `*`: 임의 길이 문자열
  - `?`: 임의 한 글자
- 현재 구현의 와일드카드 비교는 대소문자를 그대로 비교한다.
- 배제는 화면 표시와 하위 표시 대상 판정에만 적용한다.
- 비교 트리와 상태 카운트에는 배제된 항목도 내부적으로 남아 있다.
- 패턴 저장 버튼을 누르면 레지스트리에 저장하고 현재 플랫 목록을 다시 만든다.

상태 바:

```text
총 <total>개 | 동일: <id>  변경: <mo>  날짜만: <da>  왼쪽만: <lo>  오른쪽만: <ro>
```

`DateOnly`는 별도 `날짜만` 수로 표시한다.

버튼 텍스트:

- `All (<total>)`
- `Diff (<modified + dateOnly + leftOnly + rightOnly>)`
- `Diff2 (<modified + leftOnly + rightOnly>)`

### 7.10 폴더 확장과 비교 열기

- 폴더 행 더블 클릭:
  - 하위가 있으면 펼침/접힘 토글
- 폴더 행 단일 클릭:
  - 행 내부 Grid 전체에 클릭 핸들러가 붙어 있어, 하위가 있는 폴더는 사실상 행 어디를 눌러도 펼침/접힘이 토글될 수 있다.
- 파일 행 더블 클릭:
  - `CompareWindow` 열기
- 컨텍스트 메뉴의 `파일 비교`:
  - 파일이면 `CompareWindow` 열기

한쪽에만 존재하는 파일도 비교창을 열 수 있다.

- 없는 쪽 경로는 `null`
- 파일 비교창은 없는 파일을 빈 내용으로 처리한다.

### 7.11 복사 동작

메뉴:

- 왼쪽 선택 시 `→ 오른쪽으로 복사`
- 오른쪽 선택 시 `← 왼쪽으로 복사`

규칙:

- 여러 항목 동시 선택 가능
- 소스 루트 기준 상대 경로를 계산
- 대상 루트에 같은 상대 경로로 복사
- 파일은 덮어쓰기
- 폴더는 재귀 복사
- 복사 후:
  - 복사 전 확장된 폴더 경로 저장
  - 전체 비교 재실행
  - 재실행은 `Compare_Click`을 통한 2단계 비동기 비교 흐름을 사용한다.
  - 저장된 폴더 경로를 다시 확장 (`RestoreExpandedStateRecursive`)

### 7.12 삭제 동작

- `Delete` 키 또는 컨텍스트 메뉴 `삭제`
- 선택 항목을 파일과 폴더로 나눔
- 파일:
  - 파일 개수 전체에 대해 한 번 확인
  - 승인 시 각 파일 삭제
- 폴더:
  - 폴더마다 개별 확인
  - 승인 시 재귀 삭제
- 삭제 후:
  - 삭제 전 확장된 폴더 경로 저장
  - 전체 비교 재실행
  - 재실행은 `Compare_Click`을 통한 2단계 비동기 비교 흐름을 사용한다.
  - 저장된 폴더 경로를 다시 확장 (`RestoreExpandedStateRecursive`)

### 7.13 좌우 동기화

- 세로 스크롤 동기화
- 선택 인덱스 동기화
- 양쪽 목록은 같은 `_flatItems`를 공유한다.

### 7.14 마우스 선택

마우스 선택은 다음 세 가지 방식을 지원한다.

#### 단순 클릭
- 해당 항목만 선택 (기존 선택 해제)

#### Ctrl + 클릭
- 해당 항목이 선택되어 있으면 해제
- 선택되지 않았으면 추가 선택

#### Shift + 클릭
- 마지막 선택 항목부터 현재 항목까지 범위 선택

#### 드래그 선택
- 좌우 목록에서 마우스 드래그로 연속 범위 선택
- 시작 인덱스를 저장하고 이동 중 현재 인덱스까지 선택
- 현재 목록 길이가 바뀌어 시작 인덱스가 범위를 벗어나면 드래그 선택을 중단

## 8. 파일 비교창

### 8.1 창 기본값

- 제목: `File Compare`
- 크기: `1400 x 900`
- 시작 위치: owner 중앙
- 좌우 파일명은 상단에 표시
- 수정된 파일명 앞에는 `* ` 추가
- 좌우 중 하나라도 수정되면 양쪽 제목 모두에 `* ` 표시

### 8.2 화면 구조

위에서 아래 순서:

1. 파일 경로 헤더
   - 왼쪽 경로
   - 가운데 `VS`
   - 오른쪽 경로
2. 도구 모음
   - 좌측:
     - `All`
     - `Diff`
     - `Copy to Right`
     - `Insert`
     - `Save Left`
   - 우측:
     - `Copy to Left`
     - `Insert`
     - `Save Right`
     - `Undo (ESC)`
3. 본문
   - 왼쪽 줄 번호
   - 왼쪽 본문
   - 오른쪽 줄 번호
   - 오른쪽 본문
4. 편집 패널
5. 상태 바

### 8.3 파일 로드 규칙

- 경로가 `null`이거나 파일이 없으면 빈 줄 목록
- `File.ReadAllText`
- `\r\n`을 `\n`으로 정규화
- `\n` 기준으로 split

저장은 `File.WriteAllLines`를 사용하며, placeholder가 아닌 줄만 실제 파일 내용으로 기록한다.

### 8.4 Diff 알고리즘

현재 구현은 LCS 기반이다.

1. 좌우 줄 배열에 대해 LCS 테이블 계산
2. 다음 연산을 순서대로 생성
   - `Equal`
   - `Delete`
   - `Insert`
   - `Change`
3. 여러 연속 변경 구간을 하나의 묶음으로 정리
4. 같은 구간에서:
   - 좌우 모두 있으면 `Change`
   - 왼쪽만 있으면 `Delete` + 오른쪽 placeholder
   - 오른쪽만 있으면 왼쪽 placeholder + `Insert`

### 8.5 DiffLine 데이터 모델

필수 필드:

- `Text`
- `Background`
- `OriginalBackground` (선택 해제 시 원래 배경색 복원용)
- `Foreground`
- `LineNumber`
- `LineIndex`
- `IsLeft`
- `IsPlaceholder`
- `IsDifferenceRow`
- `RowVisibility`
- `RedHighlights`

`DiffLine` 클래스는 `INotifyPropertyChanged` 구현:
- `Text`와 `Background` 프로퍼티 변경 시 자동 UI 갱신
- 드래그 선택 및 컨텍스트 메뉴 동작 중 실시간 배경색 업데이트

### 8.6 행 표현

| 행 종류 | 배경 |
| --- | --- |
| Equal | 투명 |
| Delete | `ARGB(80,255,80,80)` |
| Insert | `ARGB(80,80,200,80)` |
| Change | `ARGB(80,220,200,0)` |
| Placeholder | `ARGB(30,120,120,120)` |
| Selection | `ARGB(120,80,140,240)` |

### 8.7 문자 단위 강조

`Change` 행은 좌우 텍스트에 대해:

1. 공통 접두사 길이 계산
2. 공통 접미사 길이 계산
3. 그 사이 차이 구간만 빨간색 표시

### 8.8 All / Diff 필터

- 기본값: `Diff`
- 저장값:
  - `CompareShowOnlyDiff = "1"` -> Diff
  - `CompareShowOnlyDiff = "0"` -> All
- `Diff` 모드:
  - `IsDifferenceRow = true`인 행만 보임
  - Equal 행은 UI에서 숨기지만 내부 데이터에서는 유지
- `All` 모드:
  - 모든 행 보임
- 필터 전환 시 선택 상태는 해제

초기 로드 직후 상태 바:

```text
Left: <left-line-count> lines | Right: <right-line-count> lines | Differences: <non-equal-op-count> items
```

`Differences` 값은 화면에 보이는 행 수가 아니라 LCS 연산 결과 중 `Equal`이 아닌 operation 개수다.

### 8.9 선택과 우클릭

#### 드래그 선택

- 좌우 본문에서 마우스 드래그로 연속 줄 선택
- 시작점부터 종료점까지 모든 줄 선택
- 선택된 줄은 파란색 배경(`ARGB(120,80,140,240)`) 표시
- `DiffLine` 클래스가 `INotifyPropertyChanged` 구현으로 선택 상태 실시간 반영

#### 컨텍스트 메뉴

- 우클릭 메뉴 표시
  - 왼쪽: `→ 오른쪽으로 복사`, `라인 삭제`
  - 오른쪽: `← 왼쪽으로 복사`, `라인 삭제`
- 우클릭한 줄이 기존 선택 범위 밖이면 해당 줄을 단일 선택으로 만든다.

### 8.10 줄 복사

`Copy to Right`:

- 선택이 왼쪽일 때만 동작
- 같은 인덱스의 오른쪽 줄을 왼쪽 텍스트로 교체
- 오른쪽 placeholder는 실제 줄로 전환
- undo 스택에 기존 오른쪽 텍스트와 placeholder 상태 저장
- 오른쪽 파일을 modified 상태로 표시

`Copy to Left`는 반대 방향으로 동일하다.

주의:

- `Diff` 모드에서 보이지 않는 equal 행도 내부 컬렉션에는 그대로 남는다.
- 따라서 서로 떨어진 두 diff 행 사이를 범위 선택하면, 내부 인덱스 기준으로 그 사이의 숨겨진 equal 행까지 복사 범위에 포함될 수 있다.

### 8.11 삭제

- 선택된 쪽의 각 줄에 대해:
  - 텍스트를 빈 문자열로 바꿈
  - `IsPlaceholder = true`
- 결과적으로 저장 시 해당 줄은 파일에서 빠진다.
- 원래 빈 줄이라도 삭제 가능
- undo 스택에 이전 텍스트와 placeholder 상태 저장

### 8.12 삽입

- 선택된 줄 위에 빈 줄 삽입
- 현재 선택된 쪽에만 삽입
- 새 줄은 `IsDifferenceRow = true`
- 줄 인덱스를 이후 항목에 대해 재계산
- 선택이 없으면 상태 바에 `Select a line to insert above`

현재 구현은 반대편에 대응 placeholder를 동시에 만들지 않는다.  
따라서 저장 후 다시 diff를 계산하기 전까지는 좌우 행 수가 일시적으로 달라질 수 있다.

### 8.13 클립보드

#### 복사 `Ctrl + C`

- 현재 선택 범위의 줄을 복사
- placeholder 줄은 제외
- 줄 구분자는 `Environment.NewLine`

#### 붙여넣기 `Ctrl + V`

- 텍스트 클립보드가 있어야 동작
- `\r\n`을 `\n`으로 정규화 후 split
- 선택이 있으면:
  - 선택 시작 위치부터 덮어씀
  - 붙여넣을 줄이 선택 줄보다 많으면 추가 삽입
- 선택이 없으면:
  - 현재 활성 쪽 끝에 append
- 덮어쓴 줄은 placeholder 해제
- 새로 삽입한 줄은 `IsDifferenceRow = true`
- 완료 후 새로 붙인 범위를 선택

현재 구현은 붙여넣기로 새로 추가된 행에 대해 별도 undo 액션을 만들지 않는다.

### 8.14 F2 편집 패널

#### 열기

- `F2`
- 선택이 없으면 상태 바에 `Select a line to edit`
- 선택이 있으면:
  - `Original`에 기존 텍스트 표시
  - `Edit` 텍스트박스에 기존 텍스트 표시
  - 편집 패널 표시
  - 텍스트박스 포커스
  - 전체 선택

#### 편집 입력

- 텍스트박스는 여러 줄 입력 가능
- `Enter`
  - 바로 저장 및 적용
- `Alt + Enter`
  - 현재 선택 영역을 줄바꿈으로 치환
  - 저장하지 않고 편집 계속

#### Save

- 현재 선택 줄의 텍스트를 편집값으로 교체
- placeholder 해제
- undo 스택 기록
- modified 표시
- 편집 패널 닫기
- 자동으로 diff를 다시 계산하여 비교 결과 갱신

#### Cancel

- 편집 패널만 닫음
- 값은 적용하지 않음

참고:

- 코드 안에는 과거 구현 흔적인 `EditLineDialog` 클래스가 남아 있지만, 현재 사용자 동선에서는 사용되지 않는다.
- 실제 편집 기능은 별도 팝업이 아니라 하단 인라인 편집 패널로 동작한다.

### 8.15 Undo와 취소

#### `Ctrl + Z`

- 마지막 한 건의 텍스트 변경을 undo
- 저장하는 값:
  - 이전 텍스트
  - 줄 인덱스
  - 좌우 정보
  - 이전 placeholder 상태
- undo 후 해당 파일 modified 상태 갱신

현재 구현의 undo는 텍스트 교체와 placeholder 상태 복원 중심이다.  
삽입한 새 행 자체를 제거하는 undo는 지원하지 않는다.

#### `ESC` 또는 `Undo (ESC)` 버튼

- undo 스택 전체 비움
- 파일을 디스크에서 다시 로드
- 모든 미저장 변경 폐기
- modified 플래그 해제

### 8.16 저장

파일 비교창은 생성 시 선택 파일 경로뿐 아니라 폴더 비교창의 좌우 루트 폴더도 함께 받을 수 있다. 이 루트 폴더 정보는 한쪽 파일이 없는 상태에서 새 파일 경로를 만들 때 사용한다.

공통 저장 규칙:

- 저장은 `SaveFileWithPath` 흐름을 사용한다.
- 대상 경로가 이미 있으면 그 경로에 `File.WriteAllLines`로 쓴다.
- 대상 경로가 `null`이고 반대편 파일 경로, 대상 루트 폴더, 반대편 루트 폴더가 모두 있으면:
  - 반대편 루트 기준 상대 경로를 계산한다.
  - 대상 루트 아래 같은 상대 경로로 새 대상 경로를 만든다.
  - 상위 디렉터리를 자동 생성한다.
  - 새 파일을 쓴 뒤 대상 경로 필드를 갱신한다.
- placeholder가 아닌 줄만 실제 파일에 쓴다.

#### `Ctrl + S`

- 좌우 모두 저장
- 한쪽 파일이 없는 경우 위 공통 규칙으로 새 파일 경로를 생성할 수 있다.
- 저장 후 diff를 다시 계산한다.

#### `Save Left`

- 왼쪽만 저장
- 왼쪽이 없으면 오른쪽 경로와 좌우 루트 폴더를 기준으로 생성
- 생성 경로를 판단할 수 없으면 `Cannot determine path to save left file` 메시지 표시
- 왼쪽 modified 플래그 해제
- 저장 후 diff 재계산

#### `Save Right`

- 오른쪽만 저장
- 오른쪽이 없으면 왼쪽 경로와 좌우 루트 폴더를 기준으로 생성
- 생성 경로를 판단할 수 없으면 `Cannot determine path to save right file` 메시지 표시
- 오른쪽 modified 플래그 해제
- 저장 후 diff 재계산

### 8.17 닫기

수정 사항이 하나라도 있으면 닫기 전에 묻는다.

```text
저장되지 않은 변경사항이 있습니다.
지금 저장하시겠습니까?
```

- Yes -> 둘 다 저장
- No -> 저장 없이 닫기
- Cancel -> 닫기 취소

저장 후 또는 저장 없이 닫을 때:

- 수정 상태였고 Owner가 `MainWindow`이면 `SelectAndCompareFiles(_leftPath, _rightPath)`를 호출한다.
- `SelectAndCompareFiles`는 현재 플랫 목록에서 같은 좌우 경로의 항목을 찾아 좌우 그리드에 선택한다.
- 이후 `Compare_Click()`을 비동기로 호출해 폴더 비교 결과를 다시 갱신한다.
- 닫기 질문에서 `Cancel`을 선택하면 이 흐름은 실행하지 않는다.

창이 닫히면:

- `LeftFile = ""`
- `RightFile = ""`

### 8.18 스크롤 동기화

- 왼쪽 본문 스크롤 시:
  - 오른쪽 본문
  - 왼쪽 줄 번호
  를 같은 vertical offset으로 동기화
- 오른쪽 본문도 반대 방향으로 동일

## 9. 키보드 조작 요약

### 폴더 비교창

| 키 | 동작 |
| --- | --- |
| `F5` | 대기 중에는 폴더 재비교, 비교 중에는 중지 요청 |
| `Delete` | 선택 항목 삭제 |

### 파일 비교창

| 키 | 동작 |
| --- | --- |
| `F2` | 선택 줄 편집 |
| `Ctrl + S` | 좌우 모두 저장 |
| `Ctrl + Z` | 마지막 변경 undo |
| `Ctrl + C` | 선택 줄 복사 |
| `Ctrl + V` | 붙여넣기 |
| `Delete` | 선택 줄 삭제 |
| `Insert` | 선택 줄 위 삽입 |
| `Esc` | 전체 변경 취소 및 재로드 |
| 편집 중 `Enter` | 편집 적용 |
| 편집 중 `Alt + Enter` | 줄바꿈 삽입 |

## 10. 상태 유지와 갱신

### 10.1 폴더 비교창

- 앱 시작 시 마지막 좌우 폴더 복원
- 복사/삭제/재비교 후 펼침 상태 복원
- 필터 상태는 세션 내에서만 유지

### 10.2 파일 비교창

- `All` / `Diff` 상태 저장
- 파일 저장 후 즉시 재비교
- 복사, 삭제, 삽입, 편집, 붙여넣기 시 modified 플래그 반영

## 11. 빌드와 배포

### 11.1 `dotnet build`

- 빌드 후 `copy-to-utils.ps1` 실행
- 출력 디렉터리 내용을 `D:\utils\SMFolCmp`로 복사

### 11.2 `build.ps1`

- 기본 Configuration: `Release`
- `dotnet publish -c <Configuration> -r win-x64 --self-contained`
- 기존 publish 폴더 제거
- 산출물을 `D:\utils\SMFolCmp`로 복사
- 완료 후 `SMFolCmp.exe` 실행

## 12. 현재 구현의 주의점과 동일 재현 포인트

다시 만들 때 현재 동작을 그대로 맞추려면 다음 세부 사항을 반드시 반영해야 한다.

1. 파일 비교의 `Diff` 모드는 동일 행을 데이터에서 제거하지 않고 UI에서만 숨긴다.
2. 삭제는 텍스트를 빈 문자열로 바꾸는 수준이 아니라 placeholder 처리까지 해야 실제 저장에서 줄이 빠진다.
3. 날짜만 다른 파일은 `DateOnly`이며 상태 바에서는 `날짜만` 수로 별도 표시한다.
4. 폴더 상태는 자식 폴더가 이미 집계된 상태를 기준으로 직접 자식 목록을 한 번만 훑어 다시 정한다.
5. 트리 선은 전체 원본이 아니라 현재 보이는 형제 순서를 기준으로 다시 계산한다.
6. 탐색기 단일 선택 메뉴는 `SMFolCmp with Left`와 `SMFolCmp and Compare` 두 개다.
7. 탐색기 다중 선택 메뉴는 `-SMFolCmp` 하나이며 `--compare-selected "%1"` 명령을 사용한다.
8. 다중 선택이 1개 경로만 전달되면 `PendingPath`와 `PendingTime`을 이용해 3초 윈도우 안에서 보완한다.
9. 파일 비교창은 한쪽 파일이 없을 때도 빈 파일과 비교하는 형태로 열릴 수 있다.
10. 파일 비교창의 `Esc`는 단건 undo가 아니라 전체 변경 취소다.
11. 폴더 목록의 양쪽 패널은 같은 flat list를 공유하므로 스크롤과 선택이 같은 행 기준으로 맞물린다.
12. 폴더 비교창의 경로 텍스트박스는 현재 수동 입력을 실제 비교 경로로 반영하지 않는다.
13. 파일 비교창의 `Diff` 모드에서 범위 선택은 숨겨진 equal 행을 내부적으로 포함할 수 있다.
14. 파일 비교창의 Insert와 붙여넣기 추가 행은 저장 후 재비교 전까지 반대편 placeholder를 자동 생성하지 않는다.
15. `Ctrl + Z`는 삽입 행 삭제 undo까지 완전하게 지원하지 않는다.
16. 단일 파일 비교창 생성자에 한쪽 경로가 `null`로 들어와도 빈 파일과 비교하는 방식으로 열린다.
17. single-file publish 환경에서는 `Assembly.Location`이 비어 있을 수 있어, 폴더 비교창 제목의 날짜가 실제 빌드 날짜 대신 현재 날짜로 보일 수 있다.
18. 복사/삭제 후 비교 재실행 시 이전 확장 상태를 저장했던 폴더들을 자동 복원한다.
19. 폴더 비교는 메타데이터 기반 1차 결과를 먼저 보여주고, 같은 크기 파일의 실제 내용 비교는 2차로 수행한다.
20. 2차 정밀 비교 중 중지하면 1차 빠른 비교 결과만 남길 수 있다.
21. `Diff2`는 `DateOnly`를 차이에서 제외하고 내용 변경/좌우 전용만 보여준다.
22. 파일/폴더 배제 패턴은 화면 표시용 필터이며 상태 카운트와 내부 비교 트리에는 계속 포함된다.
23. 경로 영역 드래그 앤 드롭은 첫 번째 드롭 항목이 폴더일 때만 좌우 폴더로 반영한다.
24. `DiffLine` 클래스는 `INotifyPropertyChanged` 구현으로 UI 바인딩 지원한다.
25. 드래그 선택 중 마우스가 범위를 벗어나면 `OriginalBackground`로 선택을 복원한다.
26. 파일 비교창의 수정 플래그는 좌우 중 하나라도 변경되면 양쪽 제목 모두에 `*` 표시한다.

## 13. 재구현 완료 판정 체크리스트

### 13.1 폴더 비교

- [ ] 마지막 좌우 폴더 자동 복원
- [ ] 바이너리 파일 내용 비교
- [ ] `DateOnly` 상태 구분
- [ ] 폴더 집계 상태 계산
- [ ] 폴더 우선 정렬
- [ ] 트리 확장/접기
- [ ] 트리 연결선 정확성
- [ ] All / Diff / Diff2 필터
- [ ] 파일/폴더 배제 패턴 저장 및 표시 필터
- [ ] 경로 영역 폴더 드래그 앤 드롭
- [ ] 좌우 폴더 Swap
- [ ] 비교 중 Stop/F5 취소
- [ ] 1차 메타데이터 비교와 2차 정밀 비교
- [ ] 좌우 스크롤/선택 동기화
- [ ] 드래그 범위 선택
- [ ] 복사 (확장 상태 보존)
- [ ] 삭제 (확장 상태 보존)
- [ ] 복사/삭제 후 UI 자동 새로고침
- [ ] 확장된 폴더 경로 자동 복원
- [ ] 파일 비교창 열기

### 13.2 파일 비교

- [ ] LCS 기반 줄 diff
- [ ] change/delete/insert/placeholder 정렬
- [ ] 문자 단위 빨간 강조
- [ ] All / Diff 필터 저장
- [ ] 드래그 범위 선택 (여러 줄 파란색 하이라이트)
- [ ] DiffLine INotifyPropertyChanged 구현
- [ ] 우클릭 컨텍스트 메뉴 (복사, 삭제)
- [ ] 컨텍스트 메뉴 우클릭 시 자동 선택
- [ ] 좌우 복사 (컨텍스트 메뉴)
- [ ] Delete 줄 삭제
- [ ] Insert 줄 삽입
- [ ] Ctrl+C / Ctrl+V
- [ ] F2 편집
- [ ] Enter 적용
- [ ] Alt+Enter 줄바꿈
- [ ] Ctrl+Z 단건 undo
- [ ] Esc 전체 취소
- [ ] Ctrl+S 전체 저장
- [ ] 좌/우 개별 저장
- [ ] 한쪽 파일이 없을 때 반대편 상대 경로 기준 새 파일 저장
- [ ] 파일 수정 표시 (제목에 *)
- [ ] 닫기 전 저장 확인
- [ ] 닫기 후 원본 폴더 비교창의 해당 파일 재선택 및 재비교

### 13.3 탐색기 연동

- [ ] 폴더 1개 선택 시 `SMFolCmp with Left`, `SMFolCmp and Compare` 메뉴 표시
- [ ] 파일 1개 선택 시 `SMFolCmp with Left`, `SMFolCmp and Compare` 메뉴 표시
- [ ] 폴더 2개 선택 시 `-SMFolCmp` 메뉴 하나만 표시 후 자동 비교
- [ ] 파일 2개 선택 시 `-SMFolCmp` 메뉴 하나만 표시 후 자동 비교
- [ ] `left:"%1"`, `compare:"%1"`, `--compare-selected "%1"` 명령 처리
- [ ] 2개 선택 전달이 1개씩 나뉠 때 `PendingPath`와 `PendingTime`으로 3초 이내 보완
- [ ] 3초 초과 시 새로운 첫 번째 실행으로 처리
- [ ] 다중 선택은 정확히 2개이고 같은 종류일 때만 바로 비교
- [ ] 등록 상태 판정 (폴더/파일 단일 메뉴 4개 + 폴더/파일 다중 메뉴 2개)
- [ ] 등록 해제 시 현재 키와 레거시 키까지 제거
