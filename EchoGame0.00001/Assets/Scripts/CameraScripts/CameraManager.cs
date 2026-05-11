using System;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Transform targetTransform; // target camera will follow
    private Vector3 cameraFollowVelocity = Vector3.zero;
    public float cameraFollowSpeed = 0.2f;
    private Vector3 offset;
    private void Awake()
    {
        targetTransform = FindObjectOfType<PlayerManager>().transform;
        offset = transform.position - targetTransform.position;
    }

    public void followTarget()
    {
        Vector3 targetPosition = targetTransform.position + offset;

        targetPosition = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref cameraFollowVelocity,
            cameraFollowSpeed
        );

        transform.position = targetPosition;
    }
}
