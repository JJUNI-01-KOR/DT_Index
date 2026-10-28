// Unity ↔ ROS 좌표 변환은 이 파일에서만 한다 (SRS 4.4). 담당 D · scope unity-ros
//
//            앞    왼쪽   위    손
//   Unity   +Z    -X    +Y   왼손
//   ROS     +X    +Y    +Z   오른손 (FLU = Forward, Left, Up)
//
//   예) Unity (x=1, y=2, z=3)  →  ROS (x=3, y=-1, z=2)

using UnityEngine;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;

public static class CoordConverter
{
    // ── Unity → ROS ──
    public static PointMsg ToRosPoint(Vector3 unityPosition) => unityPosition.To<FLU>();

    public static QuaternionMsg ToRosRotation(Quaternion unityRotation) => unityRotation.To<FLU>();

    public static PoseMsg ToRosPose(Vector3 unityPosition, Quaternion unityRotation) => new PoseMsg
    {
        position = unityPosition.To<FLU>(),
        orientation = unityRotation.To<FLU>(),
    };

    public static PoseMsg ToRosPose(Transform t) => ToRosPose(t.position, t.rotation);

    // ── ROS → Unity ──
    public static Vector3 ToUnityPosition(PointMsg rosPoint) => rosPoint.From<FLU>();

    public static Quaternion ToUnityRotation(QuaternionMsg rosRotation) => rosRotation.From<FLU>();
}
