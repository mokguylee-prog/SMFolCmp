---
name: comparewindow_improvements
description: CompareWindow improvements - drag-select, context menu, file modification indicator
metadata:
  type: project
---

## CompareWindow 개선 완료

### 구현된 기능 (2026-05-15)

#### 1. DiffLine INotifyPropertyChanged 추가
- `DiffLine` 클래스가 이제 `INotifyPropertyChanged` 구현
- `Text`와 `Background` 프로퍼티가 변경되면 자동으로 UI 갱신
- `OriginalBackground` 필드 추가: 선택 해제 시 원래 배경색 복원용

#### 2. 여러 줄 드래그 선택
- 필드 변경: `_selLeft/_selRight` (정수) → `_selStart/_selEnd` (범위) + `_selIsLeft` (어느 쪽)
- `LeftScroll`/`RightScroll`에 `PreviewMouseLeftButtonDown/Move/Up` 이벤트 추가
- `GetDiffLineAt()`: VisualTreeHelper를 이용해 마우스 위치에서 `DiffLine` 객체 찾기
- `UpdateSelection()`: 시작/끝 인덱스로 다중 선택 처리, 기존 선택은 `OriginalBackground`로 복원
- 마우스 드래그 중 여러 줄 선택 가능

#### 3. 우클릭 컨텍스트 메뉴
- Left ItemsControl과 Right ItemsControl의 DataTemplate 각 Border에 ContextMenu 추가
- Left: "→ 오른쪽으로 복사" + "라인 삭제"
- Right: "← 왼쪽으로 복사" + "라인 삭제"
- `ContextMenu.Opened` 이벤트: 우클릭한 줄을 자동으로 선택에 포함
  - `LeftContextMenu_Opened`: 우클릭 줄이 범위 밖이면 단일 선택으로 갱신
  - `RightContextMenu_Opened`: 동일 로직
- 복사/삭제 핸들러:
  - `CopySelectionToRight_Click`: Left의 선택 범위 텍스트를 Right로 복사
  - `CopySelectionToLeft_Click`: Right의 선택 범위 텍스트를 Left로 복사
  - `DeleteSelectedLines_Click`: 선택 범위 줄 내용을 빈 문자열로 설정

#### 4. 파일 수정 표시 (타이틀에 * 추가)
- 필드 추가: `_leftModified`, `_rightModified` (bool)
- `UpdateTitles()`: LeftTitle과 RightTitle에 `"* "` 프리픽스 추가 (수정 시)
- `UpdateLeftFile()` / `UpdateRightFile()`: 파일 내용 변경 후 `_leftModified = true`/`_rightModified = true` 설정 및 `UpdateTitles()` 호출
- `SaveFiles()`: 저장 완료 후 `_leftModified = _rightModified = false` 설정 및 `UpdateTitles()` 호출
- `LoadAndDiff()`: 시작 시 타이틀 업데이트

### 수정 파일
- `d:\work_web\SMFolCmp\SMFolCmp\Views\CompareWindow.xaml`
  - ScrollViewer 드래그 선택 이벤트 추가
  - Border ContextMenu 추가 (Left/Right 각각)
  - 버튼 Click 핸들러: CopyLineToRight_Click → CopySelectionToRight_Click (등)
- `d:\work_web\SMFolCmp\SMFolCmp\Views\CompareWindow.xaml.cs`
  - DiffLine 클래스: INotifyPropertyChanged 구현
  - 필드 리팩토링: 선택 범위 + 수정 플래그
  - 선택 관련 메서드: UpdateSelection, ClearSelection, GetDiffLineAt
  - 드래그 핸들러: LeftScroll_PreDown/PreMove/PreUp, RightScroll_PreDown/PreMove/PreUp
  - 복사/삭제: CopySelectionToRight/Left_Click, DeleteSelectedLines_Click
  - ContextMenu 핸들러: LeftContextMenu_Opened, RightContextMenu_Opened
  - 타이틀 업데이트: UpdateTitles(), OpenEditDialog() 리팩토링

### 테스트 항목
1. 라인 클릭 → 파란색 하이라이트 (INotifyPropertyChanged 효과)
2. 드래그 → 여러 줄 선택 (파란색)
3. 우클릭 → 해당 창의 컨텍스트 메뉴 표시
4. 복사 실행 → 반대편 줄 텍스트 변경 + 타이틀 `*` 표시
5. 삭제 실행 → 줄 내용 비움 + 타이틀 `*` 표시
6. Ctrl+S 저장 → 파일 저장 완료, 타이틀 `*` 사라짐
