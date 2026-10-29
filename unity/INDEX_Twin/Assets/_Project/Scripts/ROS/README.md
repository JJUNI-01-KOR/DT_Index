# ROS · 담당 D · scope unity-ros
ROS 통신과 좌표 변환은 이 폴더에서만 한다.
- TopicNames.cs : 토픽 이름 상수
- CoordConverter.cs : To<FLU>() / From<FLU>() 유일한 변환 지점
- ConnectionTest.cs : 세팅 검증용
- RosBridge.cs : 씬에 하나. UI 등이 쓰는 ROS 창구 (/factory/start 발행, /factory/state 구독)
- ConveyorRosLink.cs : 컨베이어 ↔ ROS (담당 A)
- (예정) HeartbeatLink.cs
