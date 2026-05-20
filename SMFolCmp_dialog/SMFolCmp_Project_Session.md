# SMFolCmp 프로젝트 개발 세션 기록

**날짜:** 2026-05-15  
**프로젝트:** SMFolCmp (이전명: FolderComparer)  
**작업 내용:** 프로젝트 전체 이름 변경 및 아이콘 추가

---

## 📋 작업 이력

### Phase 1: 초기 빌드 및 구조 설정
- ✅ 프로젝트 빌드 (Debug 모드)
- ✅ XAML 파일 문법 에러 수정
- ✅ C# 코드 컴파일 에러 해결
- ✅ Release 빌드 완료 (146.30 MB)
- ✅ build.ps1 스크립트 작성

### Phase 2: Windows 통합 기능 추가
- ✅ 폴더 우클릭 메뉴 통합 (컨텍스트 메뉴 등록)
- ✅ 폴더 선택 상태 저장/복구 기능
- ✅ 자동 비교 실행 기능

### Phase 3: 문서화
- ✅ README.md 작성
- ✅ 프로그램 스크린샷 추가
- ✅ 사용 방법 문서화

### Phase 4: 프로젝트 리브랜딩
- ✅ 폴더 구조 재정렬 (상대경로로 통합)
- ✅ **프로젝트명 변경: FolderComparer → SMFolCmp**
- ✅ **아이콘 생성: 빨간색 배경 + 흰색 "Cmp" 텍스트**
- ✅ 모든 Namespace 업데이트
- ✅ 모든 파일명 및 참조 변경
- ✅ Final Build 완료

---

## 🎯 최종 결과물

### 프로젝트 위치
```
D:\work_web\SMFolCmp\
├── SMFolCmp.exe                    (최종 실행 파일, 146.30 MB)
└── SMFolCmp\                       (프로젝트 폴더)
    ├── SMFolCmp.csproj
    ├── build.ps1
    ├── register_context_menu.ps1
    ├── README.md
    ├── SMFolCmp.png               (256x256 아이콘)
    ├── screenshots\
    │   └── main_window.png
    └── ... (기타 소스 코드)
```

### 변경 사항 요약

| 항목 | 이전 | 변경됨 |
|------|------|--------|
| 프로젝트명 | FolderComparer | **SMFolCmp** |
| 폴더경로 | FolderComparer/FolderComparer | **SMFolCmp/SMFolCmp** |
| Namespace | FolderComparer | **SMFolCmp** |
| 어셈블리명 | FolderComparer | **SMFolCmp** |
| 아이콘 | - | **빨간색 배경 + 흰색 "Cmp"** |
| 실행 파일 | FolderComparer.exe | **SMFolCmp.exe** |

---

## 🚀 프로그램 기능

### 주요 기능
1. **폴더 비교** - 두 폴더의 파일을 비교하여 차이 표시
2. **상태 표시**
   - ✓ Identical (동일)
   - 🔄 Modified (수정됨)
   - 🔵 Left Only (왼쪽만)
   - 🟢 Right Only (오른쪽만)
3. **파일 복사** - 마우스 우클릭으로 파일 복사
4. **텍스트 파일 비교** - 라인 단위 차이 비교
5. **컨텍스트 메뉴 통합** - 탐색기에서 직접 비교 실행

### 사용 방법

#### 방법 1: GUI 직접 사용
```
1. SMFolCmp.exe 실행
2. "Browse Left" → 왼쪽 폴더 선택
3. "Browse Right" → 오른쪽 폴더 선택
4. "Compare" 클릭
```

#### 방법 2: 컨텍스트 메뉴 사용 (권장)
```
1. 폴더 우클릭 → "Compare with SMFolCmp"
2. 다른 폴더 우클릭 → "Compare with SMFolCmp"
3. 자동으로 비교 실행
```

---

## 🔧 개발 관련 정보

### 기술 스택
- **언어:** C# 8.0
- **프레임워크:** .NET 8.0 (Windows)
- **UI:** WPF (Windows Presentation Foundation)
- **아키텍처:** MVVM 패턴

### 주요 파일
- `MainWindow.xaml/cs` - 메인 폴더 비교 화면
- `CompareWindow.xaml/cs` - 텍스트 파일 상세 비교 화면
- `Models/FileItem.cs` - 파일 정보 모델
- `build.ps1` - 자동 빌드 스크립트
- `register_context_menu.ps1` - 컨텍스트 메뉴 등록 스크립트

### 빌드 방법
```powershell
# Release 빌드
.\build.ps1

# Debug 빌드
.\build.ps1 -Configuration Debug
```

### 컨텍스트 메뉴 설정
```powershell
# 등록
.\register_context_menu.ps1

# 제거
.\register_context_menu.ps1 -Uninstall
```

---

## 📝 설정 파일

### foldercomparer.cfg
마지막으로 선택한 폴더 경로가 자동으로 저장됩니다.
```
[Left Folder Path]
[Right Folder Path]
```

---

## 💡 향후 개선 사항 (선택사항)

- [ ] 폴더 동기화 자동 기능
- [ ] 파일 필터링 옵션 (확장자별, 크기별)
- [ ] 비교 결과 내보내기 (CSV, Excel)
- [ ] 다크 모드 지원
- [ ] 실시간 폴더 감시 모드
- [ ] 파일 병합 기능

---

## 📞 문제 해결

### 컨텍스트 메뉴가 안 나타날 때
1. 관리자 권한으로 PowerShell 실행
2. `.\register_context_menu.ps1` 재실행
3. Windows 탐색기 재시작

### 프로그램이 실행 안 될 때
1. `..\SMFolCmp.exe` 존재 여부 확인
2. `.\build.ps1` 재실행으로 최신 버전 빌드

---

**작성일:** 2026-05-15  
**마지막 수정:** 2026-05-15  
**상태:** ✅ 완료
