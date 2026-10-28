// 토픽·서비스 이름 상수 (SRS 4.4). 담당 D · scope unity-ros
// 이름은 여기서만 고친다. 다른 스크립트에서 문자열로 직접 쓰지 않는다 (NFR-08).

public static class TopicNames
{
    // ── Unity → ROS2 : 제어 패널 버튼 ──
    public const string FactoryStart      = "/factory/start";           // std_msgs/Bool
    public const string EmergencyStop     = "/factory/emergency_stop";  // std_msgs/Bool (true=누름, false=해제)
    public const string PalletReset       = "/factory/pallet_reset";    // std_msgs/Bool
    public const string InjectFault       = "/factory/inject_fault";    // std_msgs/String (arm_1~4, conveyor, agv_1)
    public const string FaultClear        = "/factory/fault_clear";     // std_msgs/Bool

    // ── Unity → ROS2 : 장비 보고 ──
    public const string BinCount          = "/bin/count";               // std_msgs/Int32 (바뀔 때)
    public const string ConveyorBoxCount  = "/conveyor/box_count";      // std_msgs/Int32 (바뀔 때)
    public const string Detect1Event      = "/detect_1_event";          // factory_interfaces/DetectEvent (reject, reject_wait)
    public const string Detect2Event      = "/detect_2_event";          // factory_interfaces/DetectEvent (end_1, end_2, end_wait)
    public const string Agv1Status        = "/agv_1_status";            // std_msgs/String (IDLE, MOVING, ARRIVED, LOADED)

    // ── ROS2 → Unity ──
    public const string ConveyorCmd       = "/conveyor/cmd";            // std_msgs/Bool
    public const string Agv1Goal          = "/agv_1_goal";              // geometry_msgs/PoseStamped
    public const string FactoryState      = "/factory/state";           // std_msgs/String
    public const string FactoryStats      = "/factory/stats";           // std_msgs/String (JSON)

    // ── Heartbeat (SRS v2.1: 방향별로 분리. 1초 주기, 3초 끊기면 끊김 판정) ──
    public const string HeartbeatUnity    = "/factory/heartbeat_unity"; // std_msgs/Bool, Unity → ROS2
    public const string HeartbeatRos      = "/factory/heartbeat_ros";   // std_msgs/Bool, ROS2 → Unity

    // ── 로봇팔 (번호 1~4) ──
    public static string ArmPickPlace(int armId)  => $"/arm_{armId}_pick_place";   // factory_interfaces/PickPlace (서비스)
    public static string ArmJointStates(int armId) => $"/arm_{armId}_joint_states"; // sensor_msgs/JointState, 30 Hz

    // ── 연결 테스트 전용 ──
    public const string UnityPing         = "/unity_ping";              // std_msgs/String
}
