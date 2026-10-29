using UnityEngine;

public class DropParcel : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("NormalBox"))
        {
            // 1. 컨베이어 시스템이 박스를 움직이는 스크립트(Parcel)를 끕니다.
            MonoBehaviour parcelScript = other.GetComponent("Parcel") as MonoBehaviour;
            if (parcelScript != null) parcelScript.enabled = false;

            // 2. 물리 엔진(중력)을 켜서 미끄럼틀을 타고 떨어지게 만듭니다.
            Rigidbody rb = other.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
        }
    }
}