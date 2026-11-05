# ROS · 담당 D · scope unity-ros
ROS 통신과 좌표 변환은 이 폴더에서만 한다.
- TopicNames.cs : 토픽 이름 상수
- CoordConverter.cs : To<FLU>() / From<FLU>() 유일한 변환 지점
- ConnectionTest.cs : 세팅 검증용
- RosBridge.cs : 씬에 하나. UI 등이 쓰는 ROS 창구 (/factory/start 발행, /factory/state 구독)
- ConveyorRosLink.cs : 컨베이어 ↔ ROS (담당 A)
- (예정) HeartbeatLink.cs

## FR20 RViz 동기화

현재 ArmRig가 10 Hz로 실제 관절값을 `TopicNames.ArmJointStates(armId)`의
`/arm_N_joint_states` (`sensor_msgs/JointState`)에 발행한다. 이름은 URDF와 같은
`j1`~`j6`이며 위치는 라디안이다. `/tf`에는 `map → arm_N/base_link`를 발행한다.
베이스 월드 위치·회전은 `CoordConverter`로 FLU 변환하고 기존 AGV와 같은 UTC 시각을 쓴다.

공통 `Arm_Fairino.prefab`의 기본 ID는 1이다. Main의 팔 2·3은 씬 Override로
각각 2·3을 유지한다. 공통 프리팹 내부의 `rosBaseLink`와 관절 6개 참조를 사용하므로
Main을 다시 배치할 필요는 없다. Unpack한 팔이나 별도 프리팹은 직접 참조를 설정해야 한다.
팔별 ID와 작업 위치는 Apply All로 공통 프리팹에 적용하지 않는다.

ROS의 `arms_display.launch.py`와 팔 RobotModel을 포함한 RViz 설정이 함께 필요하다.
관절 순서: shoulder, upperarm, forearm, wrist1, wrist2, wrist3.
`rosBaseLink`가 없으면 상태 발행을 생략하고 시작 시 오류를 출력한다.
Play에서 세 팔의 ID·베이스 참조와 RViz 움직임을 확인한다.
