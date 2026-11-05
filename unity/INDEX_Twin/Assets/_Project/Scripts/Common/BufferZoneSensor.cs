using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using FactoryInterfaces = RosMessageTypes.FactoryInterfaces;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;

public class BufferZoneSensor : MonoBehaviour
{
    ROSConnection ros;
    public Transform end1_Pos; // 1번 대기칸 위치
    public Transform end2_Pos; // 2번 대기칸 위치

    private bool isEnd1Full = false;
    private bool isEnd2Full = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<FactoryInterfaces.DetectEventMsg>("/detect_2_event");
    }

    void OnTriggerEnter(Collider other)
    {
        
        if (other.CompareTag("Box"))
        {
            Rigidbody rb = other.GetComponent<Rigidbody>();
            if (rb.isKinematic) return;

            Debug.Log("센서에 닿음: " + other.name + " / 태그: " + other.tag);
            rb.isKinematic = true;

            FactoryInterfaces.DetectEventMsg msg = new FactoryInterfaces.DetectEventMsg();
            msg.box_id = other.name;
            msg.position = other.transform.position.To<FLU>();

            if (!isEnd1Full)
            {
                other.transform.position = end1_Pos.position; // 1번 칸 좌표로 박스 순간 이동
                msg.zone = "end_1";
                isEnd1Full = true;
            }
            else if (!isEnd2Full)
            {
                other.transform.position = end2_Pos.position; // 2번 칸 좌표로 박스 순간 이동
                msg.zone = "end_2";
                isEnd2Full = true;
            }
            else
            {
                msg.zone = "end_wait"; // 박스 이동 없이 대기 상태임을 ROS에 알림
            }

            ros.Publish("/detect_2_event", msg);
        }
    }
}