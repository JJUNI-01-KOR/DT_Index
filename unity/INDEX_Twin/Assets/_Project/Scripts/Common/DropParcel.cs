using UnityEngine;

// 슬라이드(경사판) 트리거: 들어온 박스를 물리로 떨어뜨린다. 담당 C · scope unity-line
// - 태그는 팀 공통 "Box" (정상·규격 초과 모두)
// - 데모 에셋 Parcel 스크립트가 붙어 있으면 꺼서 에셋 방식 이동을 멈춘다
// - 팔이 흡착 중인 박스는 KinematicGripper가 콜라이더를 꺼두므로 여기 들어오지 않는다
public class DropParcel : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Box")) return;

        // 1. 데모 에셋의 Parcel 스크립트가 있으면 끈다
        MonoBehaviour parcelScript = other.GetComponent("Parcel") as MonoBehaviour;
        if (parcelScript != null) parcelScript.enabled = false;

        // 2. 물리(중력)를 켜서 경사판을 따라 미끄러지게 한다
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }
    }
}