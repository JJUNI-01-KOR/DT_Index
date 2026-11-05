# 개발 환경 초기 세팅 (Windows + Unity / Ubuntu VM + ROS2)

> 대상: 팀원 4명 모두 · 소요: 약 2~3시간 (대부분 다운로드 대기)
> 기준: SRS v3.4 — Unity 6 (6000.6.0f1) + URP (Windows), ROS2 Humble (Ubuntu 22.04), ROS-TCP v0.7.0, 포트 10000 · 최종 확인 2026.11.05 (develop `7b7e73e`)

## 목표 구성

```
┌──────────────────────── 내 Windows PC ─────────────────────────┐
│                                                                │
│  Unity 6 (Windows, GPU)                                        │
│    ROS-TCP-Connector ── 내 VM IP:10000 (예: 192.168.0.92) ──┐   │
│                                                         │   │
│  ┌──────── Ubuntu 22.04 VM (네트워크: 어댑터에 브리지) ───▼──┐ │
│  │ ROS-TCP-Endpoint (0.0.0.0:10000)                      │ │
│  │ factory_manager · palletizer · agv_1_controller ...   │ │
│  └───────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────┘
        │ (VM이 공유기에서 IP를 직접 받음)
     [ 공유기 ]
```

- VM 네트워크는 **어댑터에 브리지**. VM이 공유기에서 IP를 직접 받는다.
- Unity ROS Settings에는 **각자 자기 VM IP**를 적는다 → 사람마다 값이 다르다.
- 그 값이 저장되는 파일 `Assets/Resources/ROSConnectionPrefab.prefab`은 **각자 PC에서 Git이 무시하도록(skip-worktree)** 해 둔다 → 서로 덮어쓰지 않는다. `scripts/setup-git-hooks.sh`가 자동으로 설정한다.
- 브리지에서는 4명의 VM이 같은 네트워크에 보이므로 **`ROS_LOCALHOST_ONLY=1`이 필수**다.

## 작업 순서 한눈에 보기

| 단계 | 내용 | 어디서 | 시간 |
|---|---|---|---|
| 0 | PC 사양·가상화 확인 | Windows | 5분 |
| 1 | Git, Unity Hub, Unity 6, VirtualBox 설치 | Windows | 40분 |
| 2 | VM 만들기 (브리지) + Ubuntu 설치 | VirtualBox | 30분 |
| 3 | 게스트 확장 + ROS2 Humble 설치 | VM | 30분 |
| 4 | Git·SSH 설정, 저장소 클론, 빌드 | VM + Windows | 15분 |
| 5 | 연결 테스트 | 둘 다 | 10분 |

> 팀 저장소 없이 연결만 확인하려면 5-A "빈 프로젝트 테스트"를, 팀 저장소로 확인하려면 5-B를 한다.

---

## 0. 사전 확인 (Windows)

| 항목 | 권장 | 최소 | 확인 방법 |
|---|---|---|---|
| OS | Windows 10/11 64bit | | 설정 → 시스템 → 정보 |
| RAM | 16 GB | 12 GB | 작업 관리자 → 성능 → 메모리 |
| 디스크 여유 | 100 GB | 70 GB | Unity 약 15 GB, 프로젝트 약 10 GB, VM 50 GB |
| 가상화 | **사용** | | 작업 관리자 → 성능 → CPU → "가상화: 사용" |

- "가상화: 사용 안 함"이면 BIOS에서 **Intel VT-x / AMD SVM**을 켠다 (제조사별로 메뉴 위치가 다름).
- **경로에 한글·공백을 쓰지 않는다.** 작업 폴더는 `C:\dev\`로 통일한다. Windows 사용자 이름이 한글이어도 이 폴더를 쓰면 문제없다.

---

## 1. Windows 설치

### 1-1. Git for Windows (Git LFS 포함)
1. https://git-scm.com 에서 설치 (기본 옵션으로 진행)
2. **Git Bash**를 열고:
```bash
git lfs install
git config --global user.name  "이름"
git config --global user.email "GitHub 이메일"
git config --global core.autocrlf true        # Windows 줄바꿈 자동 변환
git config --global core.longpaths true       # Unity 긴 경로 대응
ssh-keygen -t ed25519 -C "GitHub 이메일"       # 엔터 3번
cat ~/.ssh/id_ed25519.pub                     # → GitHub Settings → SSH keys에 등록
ssh -T git@github.com                         # "Hi 아이디!" 확인
```

### 1-2. Unity
1. **Unity Hub** 설치 → 로그인 → 개인 라이선스 활성화
2. Installs → Install Editor → **팀 고정 버전 `6000.6.0f1`** (D가 정해 공지. 4명 모두 정확히 같은 버전)
   - 목록에 없으면 Archive에서 해당 버전 → "Unity Hub로 설치"
   - 모듈: **Microsoft Visual Studio Community 2022** 체크 (C# 편집·디버깅용)
3. ⚠ 프로젝트를 열 때 **"Upgrade" / "Change Editor version" 창이 뜨면 절대 진행하지 않는다.** 버전을 맞춰 설치한 뒤 다시 연다.

### 1-3. VirtualBox
1. https://www.virtualbox.org → **VirtualBox 7.x Windows hosts** 설치
2. Ubuntu ISO 다운로드: **ubuntu-22.04.x-desktop-amd64.iso** (https://releases.ubuntu.com/jammy/)

---

## 2. VM 만들기 + Ubuntu 설치

### 2-1. VM 생성 (VirtualBox → 새로 만들기)

| 항목 | 값 |
|---|---|
| 이름 | `ROS2-Humble` |
| 폴더 | `C:\dev\VMs` |
| ISO 이미지 | 위에서 받은 22.04 ISO |
| **무인 설치 건너뛰기** | **체크** (자동 설치를 쓰면 sudo 권한이 빠지는 경우가 있음) |
| 메모리 | RAM 16 GB PC → **6144 MB** / 12 GB PC → 4096 MB |
| CPU | 4개 (최소 2개) |
| 디스크 | 50 GB, VDI, 동적 할당 |

생성 후 **설정**에서:
- 디스플레이 → 비디오 메모리 128 MB
- 일반 → 고급 → 클립보드 공유: **양방향**

### 2-2. 네트워크: 어댑터에 브리지 (핵심)

VM을 **끈 상태에서** 설정 → 네트워크 → **어댑터 1**

| 항목 | 값 |
|---|---|
| 네트워크 어댑터 사용하기 | ☑ |
| 다음에 연결됨 | **어댑터에 브리지** |
| 이름 | **지금 인터넷에 연결된 Windows 어댑터** (데스크탑 랜선이면 `Realtek ... Ethernet` 류, 와이파이면 `Wi-Fi` 류) |
| 고급 → 무작위 모드 | 거부 (기본값) |
| 고급 → 케이블 연결됨 | ☑ |

> 어댑터 이름을 모르겠으면 Windows PowerShell에서 `Get-NetAdapter | Where-Object Status -eq Up` → Status가 Up인 것.
> 이후 랜선 ↔ 와이파이를 바꾸면 여기 이름도 바꿔야 한다.

VM이 부팅되면 공유기에서 IP를 직접 받는다 (3-5에서 확인).

### 2-3. Ubuntu 설치
1. VM 시작 → **Install Ubuntu**
2. 언어: **English** (한국어로 설치하면 `~/바탕화면` 같은 한글 폴더가 생겨 경로 오류가 날 수 있음. 한글 입력은 나중에 추가)
3. **Minimal installation** 선택, "Download updates" 체크
4. "Erase disk and install Ubuntu" (VM 안의 가상 디스크라서 Windows에는 영향 없음)
5. 사용자 이름은 **영문 소문자** (예: `index`)

---

## 3. VM 안에서: 게스트 확장 + ROS2

### 3-1. 기본 패키지 + 게스트 확장
```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y build-essential dkms linux-headers-$(uname -r) git git-lfs curl software-properties-common
```
VirtualBox 메뉴 → 장치 → **게스트 확장 CD 이미지 삽입** → 화면에서 Run → 재부팅
→ 창 크기에 맞춰 화면이 바뀌고 복사·붙여넣기가 되면 성공

### 3-2. ROS2 Humble 설치
```bash
# 로케일
sudo apt install -y locales
sudo locale-gen en_US en_US.UTF-8
sudo update-locale LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8
export LANG=en_US.UTF-8

# 저장소 등록
sudo add-apt-repository universe -y
export ROS_APT_SOURCE_VERSION=$(curl -s https://api.github.com/repos/ros-infrastructure/ros-apt-source/releases/latest | grep -F "tag_name" | awk -F\" '{print $4}')
curl -L -o /tmp/ros2-apt-source.deb "https://github.com/ros-infrastructure/ros-apt-source/releases/download/${ROS_APT_SOURCE_VERSION}/ros2-apt-source_${ROS_APT_SOURCE_VERSION}.$(. /etc/os-release && echo $VERSION_CODENAME)_all.deb"
sudo dpkg -i /tmp/ros2-apt-source.deb

# 설치 (약 20분)
sudo apt update && sudo apt upgrade -y
sudo apt install -y ros-humble-desktop ros-dev-tools

# rosdep (처음 1번)
sudo rosdep init
rosdep update
```

### 3-3. `~/.bashrc` 등록
```bash
cat >> ~/.bashrc << 'EOF'

# ===== INDEX 프로젝트 =====
source /opt/ros/humble/setup.bash
source /usr/share/colcon_argcomplete/hook/colcon-argcomplete.bash
export ROS_LOCALHOST_ONLY=1      # ⚠ 브리지에서 필수: 내 VM 안에서만 ROS 통신 (옆 사람 VM 노드와 섞이지 않음)
export ROS_DOMAIN_ID=30          # 팀 공통 값
# 아래는 4단계 빌드 후 주석 해제
# source ~/dev/DT_Index/ros2_ws/install/setup.bash
EOF
source ~/.bashrc
```

### 3-4. 설치 확인
```bash
ros2 run demo_nodes_cpp talker          # 터미널 1
ros2 run demo_nodes_py listener         # 터미널 2 → "I heard: Hello World"
```

### 3-5. 내 VM IP 확인 (중요)
```bash
ip -4 addr show
```
```
2: enp0s3: ...
    inet 192.168.0.92/24 ...     ← 이 숫자가 "내 VM IP" (사람마다 다름)
```
- `192.168.x.x`처럼 **Windows와 앞자리가 같은 주소**가 나오면 정상 (Windows에서 `ipconfig`로 비교).
- `10.0.2.15`가 나오면 아직 NAT 상태 → 2-2를 다시 확인하고 VM 재부팅.
- 아무 주소도 없으면 Ubuntu 설정 → Network → Wired를 껐다 켠다.

이 IP는 5단계에서 Unity ROS Settings에 적는다. **공유기가 바꿀 수 있으니** 연결이 안 되면 제일 먼저 다시 확인한다. (자주 바뀌면 공유기 관리 페이지에서 VM의 MAC 주소로 IP 고정)

Windows에서 VM이 보이는지 확인 (PowerShell):
```powershell
ping 192.168.0.92          # 내 VM IP
```

### 3-6. VS Code (VM)
```bash
sudo snap install code --classic
```
확장: Python, YAML, GitLens, ROS 2 지원 확장

---

## 4. Git 설정 + 저장소 클론

> **Windows와 VM에 저장소를 각각 클론한다.** 공유 폴더로 하나를 같이 쓰지 않는다 (권한·심볼릭 링크·속도 문제로 `colcon build`가 깨진다).
>
> | 위치 | 용도 | 주로 커밋하는 것 |
> |---|---|---|
> | Windows `C:\dev\DT_Index` | Unity 작업 | `unity/` |
> | VM `~/dev/DT_Index` | ROS 작업 | `ros2_ws/` |
>
> 같은 원격 저장소라서 양쪽 모두 push·pull 한다. **작업을 시작하기 전에 양쪽 다 `git pull`.**

### 4-1. VM
```bash
git lfs install
git config --global user.name  "이름"
git config --global user.email "GitHub 이메일"
git config --global core.autocrlf input
ssh-keygen -t ed25519 -C "GitHub 이메일"   # → 이 키도 GitHub에 등록 (Windows 키와 별개)
ssh -T git@github.com

mkdir -p ~/dev && cd ~/dev
GIT_LFS_SKIP_SMUDGE=1 git clone git@github.com:JJUNI-01-KOR/DT_Index.git
#  ↑ VM에서는 Unity 대용량 파일(LFS)을 받지 않음 → 시간·용량 절약
cd DT_Index
bash scripts/setup-git-hooks.sh

# 중간 다리 ROS-TCP-Endpoint (dependencies.repos에 ROS2v0.7.0으로 고정됨)
cd ros2_ws
vcs import src < dependencies.repos

# 빌드
rosdep install --from-paths src --ignore-src -y
colcon build --symlink-install
```
그리고 `~/.bashrc`에서 워크스페이스 `source` 줄의 주석을 해제한다 → `source ~/.bashrc`

### 4-2. Windows (Git Bash)
```bash
cd /c/dev
git clone git@github.com:JJUNI-01-KOR/DT_Index.git
cd DT_Index
bash scripts/setup-git-hooks.sh
#  ↑ 훅 등록 + ROSConnectionPrefab.prefab skip-worktree 설정까지 자동
#    "✔ ROS 설정 파일 skip-worktree 적용" 이 보여야 한다
```
Unity Hub → Add → `C:\dev\DT_Index\unity\INDEX_Twin` → **팀 고정 버전**으로 열기
(처음 열 때 Library 생성에 10~20분 걸림)

---

## 5. 연결 테스트

### 5-A. 팀 프로젝트가 나오기 전: 빈 프로젝트로 혼자 테스트 (지금 가능)

**Windows**
1. Unity Hub → New project → **Universal 3D (URP)** → `C:\dev\RosTest`
2. Window → Package Manager → `+` → Add package from git URL:
   `https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.ros-tcp-connector#v0.7.0`
3. Robotics → ROS Settings: **Protocol = ROS2**, ROS IP = **내 VM IP** (3-5에서 확인, 예: `192.168.0.92`), Port = **10000**
4. 빈 GameObject에 아래 스크립트를 붙인다:

```csharp
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class RosPingTest : MonoBehaviour
{
    ROSConnection ros;
    float t;
    int n;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>("/unity_ping");             // Unity → ROS
        ros.Subscribe<BoolMsg>("/conveyor/cmd", m =>                  // ROS → Unity
            Debug.Log($"[ROS→Unity] conveyor cmd = {m.data}"));
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t < 1f) return;
        t = 0f;
        ros.Publish("/unity_ping", new StringMsg($"hello from Unity {n++}"));
    }
}
```

**VM**
```bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0
```

**확인**
| 방향 | 방법 | 성공 기준 |
|---|---|---|
| 연결 | Unity Play → 왼쪽 위 HUD | 화살표가 파란색 (빨간색이면 연결 실패) |
| Unity → ROS | VM 새 터미널: `ros2 topic echo /unity_ping` | `hello from Unity 0, 1, 2...` |
| ROS → Unity | `ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"` | Unity Console에 `conveyor cmd = True` |

> 팀 저장소로 작업할 때는 4-1 과정으로 받은 Endpoint를 `dt_bringup` launch로 띄운다 (5-B).

### 5-B. 팀 프로젝트로 테스트

먼저 Unity → **Robotics → ROS Settings** → ROS IP Address에 **내 VM IP**를 적는다.
(이 값은 skip-worktree 덕분에 Git에 올라가지 않는다. `git status`에 `ROSConnectionPrefab.prefab`이 안 보이면 정상)

| # | 어디서 | 명령 / 동작 | 성공 기준 |
|---|---|---|---|
| 1 | VM | `ros2 launch dt_bringup connect.launch.py` | `Starting server on 0.0.0.0:10000` |
| 2 | Unity | `Assets/_Project/Scenes/Sandbox/ConnectionTest` 씬 Play | HUD 화살표 파란색 |
| 3 | VM | `ros2 topic echo /unity_ping` | `ping 1, 2, 3...` |
| 4 | VM | `ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"` | Unity Console에 `= True` |

4개 모두 통과하면 연결 세팅은 끝이다. 전체 공정 실행은 루트 [README.md](../README.md)의 "빠른 시작"을 따른다.

```bash
ros2 launch dt_bringup factory.launch.py                 # VM: 전체 시연 (Unity Main 씬 Play → [Clear] → [Start])
ros2 launch dt_description factory_display.launch.py     # VM: (선택) RViz 표시
```
launch는 창을 닫지 말고 `Ctrl+C`로 끈다. 창을 닫으면 Endpoint가 포트 10000을 쥔 채 남을 수 있다.

---

## 6. D가 1번만 한 일 (팀 저장소 준비, 10/28 완료)

다른 팀원은 클론만 하면 되도록 D가 미리 커밋했다. 저장소를 새로 만들 때 참고한다.

- [x] Unity 버전 확정 후 공지, 프로젝트 생성 (`unity/INDEX_Twin`, URP)
- [x] Project Settings → Editor: **Asset Serialization = Force Text**, **Version Control = Visible Meta Files**
- [x] Package Manager: ROS-TCP-Connector v0.7.0, URDF Importer v0.5.2 추가 → `Packages/manifest.json` 커밋
- [x] ROS Settings: ROS2 / **D의 VM IP** / 10000 → **처음 1번만** 커밋 (값은 무엇이든 상관없음. 파일이 저장소에 있어야 skip-worktree를 걸 수 있고, 팀원은 각자 자기 IP로 덮어씀)
- [x] 커밋 후 `bash scripts/setup-git-hooks.sh` → 이후 내 VM IP를 적어도 Git에 안 올라감
- [x] Robotics → Generate ROS Messages → `ros2_ws/src/factory_interfaces` 선택 → Build → `Assets/RosMessages/` 커밋
- [x] `.gitignore` (Unity `Library/ Temp/ Logs/ UserSettings/`, ROS `build/ install/ log/`, `ROS-TCP-Endpoint/`, `Assets/ThirdParty/`)
- [x] `.gitattributes` (LFS: `*.fbx *.png *.psd *.wav *.stl *.dae` 등, `* text=auto`)
- [x] `scripts/setup-git-hooks.sh`, `docs/` 문서

---

## 7. 문제 해결

| 증상 | 원인 | 해결 |
|---|---|---|
| HUD 화살표 빨간색 | Endpoint 미실행 / VM IP가 바뀜 / ROS IP 오타 | VM에서 Endpoint 실행 확인 → VM `ip -4 addr`로 IP 재확인 → ROS Settings 수정 → PowerShell `Test-NetConnection <VM IP> -Port 10000` |
| VM IP가 `10.0.2.15` | 아직 NAT | VM 끄고 2-2 브리지로 변경 → 재부팅 |
| VM에 IP가 안 잡힘 | 브리지 어댑터 이름이 틀림 (안 쓰는 랜카드 선택) | 2-2에서 지금 연결된 어댑터로 변경 |
| 학교 와이파이에서 브리지가 안 됨 | 일부 와이파이는 기기당 1개 주소만 허용 | 랜선 연결 또는 팀 공유기 사용 |
| `git status`에 `ROSConnectionPrefab.prefab`이 보임 | skip-worktree 미적용 | `bash scripts/setup-git-hooks.sh` 다시 실행 |
| pull 했더니 `ROSConnectionPrefab.prefab` 때문에 에러 | 누군가 이 파일을 커밋함 | 단톡에 알림 → 아래 "skip-worktree 풀고 받기" 참고 |
| `Bool`은 되는데 `PickPlace` 서비스만 안 됨 | Endpoint를 띄운 터미널에서 워크스페이스를 source 안 함 | `source ~/dev/DT_Index/ros2_ws/install/setup.bash` 후 launch 다시 실행 |
| launch를 다시 켜니 `Address already in use` (10000) | 이전 launch 창을 닫아 Endpoint가 남음 | `pkill -f tcp_endpoint_mt` 후 다시 실행. 끌 때는 `Ctrl+C` |
| `ros2 topic list`에 다른 사람 노드가 보임 | `ROS_LOCALHOST_ONLY` 미설정 (브리지라서 옆 VM이 보임) | `.bashrc`에 `export ROS_LOCALHOST_ONLY=1` → 새 터미널 |
| VM이 매우 느림, 상태 표시줄에 **거북이 아이콘** | Windows Hyper-V 위에서 실행 중 | Windows 기능에서 "Hyper-V", "가상 머신 플랫폼" 끄기 → 재부팅 (WSL2·Docker도 같이 꺼짐) |
| Unity가 "Upgrade project?" | 에디터 버전 불일치 | 취소 → 팀 고정 버전 설치 후 열기 |
| 경로 관련 빌드 오류 | 경로에 한글·공백 | `C:\dev\`, VM `~/dev/`로 이동 |
| `colcon build` 중 VM 멈춤 | 메모리 부족 | `colcon build --parallel-workers 1` 또는 VM RAM 늘리기 |
| 커밋 거부 (pre-commit / commit-msg) | main·develop에서 커밋했거나 메시지 형식 위반 | 작업 브랜치로 이동, `COMMIT_RULES.md` 형식 확인 (`feat(scope): 내용`) |

---

## 8. 개인 체크리스트

| # | 항목 | 권오민 | 김예은 | 김찬중 | 박준영 |
|---|---|---|---|---|---|
| 0 | 사양·가상화 확인 | ☐ | ☐ | ☐ | ☐ |
| 1 | Git·SSH (Windows) | ☐ | ☐ | ☐ | ☐ |
| 1 | Unity 6 팀 고정 버전 | ☐ | ☐ | ☐ | ☐ |
| 2 | VM 브리지 + 내 VM IP 확인 | ☐ | ☐ | ☐ | ☐ |
| 4 | skip-worktree 적용 확인 | ☐ | ☐ | ☐ | ☐ |
| 3 | ROS2 talker/listener | ☐ | ☐ | ☐ | ☐ |
| 4 | 저장소 클론·빌드 (양쪽) | ☐ | ☐ | ☐ | ☐ |
| 5-A | 빈 프로젝트 양방향 통신 | ☐ | ☐ | ☐ | ☐ |
| 5-B | 팀 프로젝트 연결 | ☐ | ☐ | ☐ | ☐ |

---

## 부록. skip-worktree 다루기

```bash
# 적용 여부 확인 (S 로 시작하면 적용됨)
git ls-files -v | grep ROSConnectionPrefab
#  S unity/INDEX_Twin/Assets/Resources/ROSConnectionPrefab.prefab

# 수동 적용 (setup-git-hooks.sh가 자동으로 해 줌)
git update-index --skip-worktree unity/INDEX_Twin/Assets/Resources/ROSConnectionPrefab.prefab

# 누군가 이 파일을 커밋해서 pull이 막힐 때: 풀고 → 받고 → 내 IP 다시 적고 → 다시 걸기
git update-index --no-skip-worktree unity/INDEX_Twin/Assets/Resources/ROSConnectionPrefab.prefab
git restore unity/INDEX_Twin/Assets/Resources/ROSConnectionPrefab.prefab
git pull
git update-index --skip-worktree unity/INDEX_Twin/Assets/Resources/ROSConnectionPrefab.prefab
#  → Unity ROS Settings에 내 VM IP 다시 입력
```
