// 토픽·서비스 이름 상수 (SRS v3.1 4.4). 담당 D · scope unity-ros
// 이름은 여기서만 고친다. 다른 스크립트에서 문자열로 직접 쓰지 않는다 (NFR-08).
// Unity가 직접 주고받는 것만 둔다. ROS 노드끼리만 쓰는 토픽(/factory/fault, /factory/retry,
// /pallet_N_placed, /pallet_N_count, /agv_1_cmd, /agv_1_loaded)은 여기 없다.

public static class TopicNames
{
    // ── Unity → ROS2 : 제어 패널 ──
    public const string FactoryStart      = "/factory/start";           // std_msgs/Bool, [시작] true (FR-01)
    public const string EmergencyStop     = "/factory/emergency_stop";  // std_msgs/Bool, [비상정지] 누름 true, 해제 false (FR-16)
    public const string FaultClear        = "/factory/fault_clear";     // std_msgs/String, 3초 조치를 마친 장비 id: arm_1~3, conveyor (FR-18)
    public const string InjectFault       = "/factory/inject_fault";    // std_msgs/String, [고장 주입] 대상 장비 id: arm_1~3, conveyor (FR-19, 선택)

    // ── Unity → ROS2 : 장비 보고 ──
    public const string BinCount          = "/bin/count";               // std_msgs/Int32, 적재함 남은 방석 수. 바뀔 때 + 1초마다 (FR-05)
    public const string Scale1Weight      = "/scale_1_weight";          // std_msgs/String (JSON) {"id":"C012","kg":1.02}. 방석당 1회 (FR-06)
    public const string Conveyor1Load     = "/conveyor_1_load";         // std_msgs/String (JSON) {"main":1,"ok":2,"ng":0}. 바뀔 때와 1초마다 (FR-04)
    public const string Conveyor1EndOk    = "/conveyor_1_end_ok";       // std_msgs/String, 정상 레인 집는 자리에 들어온 방석 id. 방석당 1회 (FR-09)
    public const string Conveyor1EndNg    = "/conveyor_1_end_ng";       // std_msgs/String, 불량 레인 집는 자리에 들어온 방석 id. 방석당 1회 (FR-09)
    public static string PalletStatus(int n) => $"/pallet_{n}_status";  // std_msgs/String, READY · OUT · REFILLING. 바뀔 때와 1초마다 (FR-15), n = 1, 2
    public const string Agv1Status        = "/agv_1_status";            // std_msgs/String (JSON) {"state":"ARRIVED","wp":"P1_DOCK"}. MOVING · ARRIVED · ATTACHED · DETACHED. 바뀔 때 (FR-13)

    // ⚠ v2.2 이름. ConveyorLink(A)가 Conveyor1Load로 바뀌면 지운다
    public const string ConveyorBoxCount  = "/conveyor/box_count";      // std_msgs/Int32

    // ── ROS2 → Unity ──
    public const string FactoryState      = "/factory/state";           // std_msgs/String, IDLE · RUNNING · ERROR · E_STOP. 1초마다와 바뀔 때
    public const string FactoryStats      = "/factory/stats";           // std_msgs/String (JSON), 상태 패널 표시값 (부록 A.4)
    public const string ConveyorCmd       = "/conveyor/cmd";            // std_msgs/Bool, 가동 true · 정지 false. 메인 라인과 두 레인 함께 (FR-03)
    public const string Diverter1Cmd      = "/diverter_1_cmd";          // std_msgs/String (JSON) {"id":"C012","lane":"ok"}. ok 왼쪽 · ng 오른쪽 (FR-08)
    public const string Agv1Goal          = "/agv_1_goal";              // std_msgs/String (JSON) {"wp","x","y","yaw"} 이동 · {"hitch":"attach"|"detach"} (FR-13)

    // ── Heartbeat (1초 주기, 3초 끊기면 끊김 판정, FR-17) ──
    public const string HeartbeatUnity    = "/factory/heartbeat_unity"; // std_msgs/Bool, Unity → ROS2
    public const string HeartbeatRos      = "/factory/heartbeat_ros";   // std_msgs/Bool, ROS2 → Unity

    // ── 로봇팔 서비스 (번호 1~3, Unity가 서버) ──
    public static string ArmPickPlace(int armId) => $"/arm_{armId}_pick_place";   // factory_interfaces/PickPlace. 20초 무응답이면 호출 쪽이 실패 처리

    // ── 연결 테스트 전용 ──
    public const string UnityPing         = "/unity_ping";              // std_msgs/String
}
