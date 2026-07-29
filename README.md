# 팀 개발을 위한 Git 브랜치 전략 및 워크플로우 가이드

본 문서는 개인 개발 프로젝트를 팀 협업 프로젝트로 전환할 때 적용하는 **Git Flow (변형) 브랜치 전략**과 **단계별 작업 절차**, **필수 명령어 정리**를 담고 있습니다.

---

## 1. 브랜치 구조 및 기본 개념

| 브랜치명 | 역할 | 설명 |
| :--- | :--- | :--- |
| **`main`** (또는 `master`) | 배포용 (Production) | 언제든 사용자에게 서비스할 수 있는 최종 안정 상태의 브랜치입니다. |
| **`develop`** | 통합 개발 (Development) | 다음 배포를 위해 완성된 기능들이 모이는 중심 브랜치입니다. |
| **`feat/*`** 또는 **`feature/*`** | 기능 개발 (Feature) | 단위 기능(예: `feat/ribbon`, `feat/login`)을 개별적으로 개발하는 브랜치입니다. |
| **`fix/*`** 또는 **`bugfix/*`** | 버그 수정 (Fix) | 개발 진행 중 발견된 버그를 수정하는 브랜치입니다. |

---

## 2. 초기 세팅 가이드 (1회 진행)

### 2.1 기존 프로젝트 소유자 (내 로컬 & 원격)

1. **기존 `main` 브랜치 최신화 및 `develop` 생성**

   ```bash
   git checkout main
   git pull origin main
   git checkout -b develop
   ```

2. **원격 저장소로 develop push 및 추적 설정**

   ```bash
   git push -u origin develop
   ```

3. **원격 저장소 (GitHub / GitLab) 권장 설정**
   - **Default Branch 변경**: 저장소 설정(Settings ➔ Branches)에서 기본 브랜치를 `develop`으로 변경합니다.
   - **Branch Protection Rules 설정**: `main`과 `develop` 브랜치에 직접 push를 금지하고, 최소 1명 이상의 승인(Approve)을 포함한 Pull Request (PR)를 통해서만 병합되도록 설정합니다.

### 2.2 새로 합류하는 동료 (타인 로컬)

1. **저장소 클론 (Clone)**

   ```bash
   git clone <원격 저장소 URL>
   cd <프로젝트 폴더명>
   ```

2. **develop 브랜치 확인 및 이동**

   ```bash
   git checkout develop
   git pull origin develop
   ```

---

## 3. 기능 개발 워크플로우 (매 기능 단위 반복)

### 1단계: 작업 시작 전 develop 동기화

새로운 기능을 개발하기 전 항상 로컬의 develop을 원격의 최신 코드와 맞춥니다.

```bash
git checkout develop
git pull origin develop
```

### 2단계: 신규 기능 브랜치 생성

구현할 기능 명칭을 명확하게 붙여 브랜치를 생성합니다 (예: `feat/ribbon`).

```bash
git checkout -b feat/ribbon
```

### 3단계: 코드 작성 및 상태 점검

작업 중간중간 변경된 파일 상태와 수정 내역을 점검합니다.

```bash
# 변경된 파일 목록 확인
git status

# 코드 수정 내역 상세 확인
git diff
```

### 4단계: 커밋 (Commit) 및 원격 Push

작업이 완료되면 의미 있는 단위로 커밋한 후 원격 저장소에 올립니다.

```bash
git add .
git commit -m "feat: 리본 UI 컴포넌트 추가 및 이벤트 연동"
git push -u origin feat/ribbon
```

### 5단계: Pull Request (PR) 및 코드 리뷰

- GitHub/GitLab 웹 사이트에서 `feat/ribbon` ➔ `develop` 방향으로 PR을 생성합니다.
- 팀원에게 코드 리뷰를 요청합니다.
- 피드백 반영 후 승인(Approve)을 받으면 `develop` 브랜치로 병합(Merge)을 완료합니다.

### 6단계: 로컬 브랜치 정리 및 최신화

병합이 완료되면 로컬로 돌아와 최신 코드를 받고, 사용이 끝난 기능 브랜치를 삭제합니다.

```bash
git checkout develop
git pull origin develop
git branch -d feat/ribbon
```

---

## 4. 필수 명령어 체계적 정리

### 🔍 상태 확인 및 이력 조회

- `git status` : 현재 작업 트리 상태(수정/추가된 파일 목록) 확인
- `git branch` : 로컬 브랜치 목록 확인
- `git branch -a` : 로컬 및 원격 저장소의 모든 브랜치 목록 확인
- `git log --oneline --graph --all` : 브랜치 흐름과 커밋 이력을 한눈에 시각화하여 확인
- `git diff` : 스테이징 되기 전 수정된 코드의 상세 차이점 확인

### 🔄 동기화 및 이동

- `git fetch` : 원격 저장소의 최신 변경 사항만 가져옴 (로컬 코드에는 영향 없음)
- `git pull origin <브랜치명>` : 원격 저장소의 변경 사항을 가져와 현재 브랜치에 병합
- `git checkout <브랜치명>` : 다른 브랜치로 이동
- `git checkout -b <새브랜치명>` : 새 브랜치를 생성함과 동시에 이동

### 🧹 브랜치 삭제 및 정리

- `git branch -d <브랜치명>` : 병합이 완료된 로컬 브랜치 삭제
- `git branch -D <브랜치명>` : 강제 로컬 브랜치 삭제 (주의)
- `git push origin --delete <브랜치명>` : 원격 저장소의 브랜치 삭제

---

## 5. 협업 규칙 (Convention) 가이드

### 커밋 메시지 컨벤션 (Commit Message Convention)

- `feat:` 새로운 기능 추가
- `fix:` 버그 수정
- `docs:` 문서 수정 (README 등)
- `style:` 코드 포맷팅, 세미콜론 누락 등 (코드 로직 변경 없음)
- `refactor:` 코드 리팩토링 (기능 추가나 버그 수정이 없는 구조 개선)
- `chore:` 빌드 업무 수정, 패키지 매니저 수정 등

### 브랜치 명명 규칙 (Branch Naming Rule)

- `feat/<기능명>` (예: `feat/ribbon`, `feat/user-auth`)
- `fix/<버그명>` (예: `fix/header-overflow`)
- `docs/<문서명>` (예: `docs/git-guide`)
