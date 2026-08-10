using System;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    InputManager inputManager;
    public Transform targetTransform; // target camera will follow
    public Transform cameraPivot;     // object camera uses to pivot
    private Transform cameraTransform; // the actual camera child

    private Vector3 cameraFollowVelocity = Vector3.zero;
    public float cameraFollowSpeed = 0.2f;
    // Mouse: degrees per pixel of delta. Mouse delta is already a per-frame
    // measurement, so these must NOT be scaled by deltaTime.
    public float cameraLookSpeed = 2f;
    public float cameraPivotSpeed = 2f;
    // Stick: degrees per second. The stick reports a held position rather than
    // a movement, so these MUST be scaled by deltaTime or the turn rate ends up
    // riding on the frame rate.
    public float stickLookSpeed = 180f;
    public float stickPivotSpeed = 120f;
    public float lookAngle;
    public float pivotAngle;
    public float minimumPivotAngle = -35f;
    public float maximumPivotAngle = 35f;

    // Camera collision
    public float cameraCollisionRadius = 0.2f;   // how large the sphere is
    public float cameraCollisionOffset = 0.2f;   // minimum gap between camera and surface
    public LayerMask collisionLayers = -1;        // what to collide with (-1 = everything)
    private float defaultCameraZOffset;           // original z distance stored on start
    private float targetCameraZOffset;            // z offset we are moving toward

    private void Awake()
    {
        inputManager = FindObjectOfType<InputManager>();
        targetTransform = FindObjectOfType<PlayerManager>().transform;
        cameraTransform = Camera.main.transform;
        defaultCameraZOffset = cameraTransform.localPosition.z;
        targetCameraZOffset  = defaultCameraZOffset;
    }

    public void HandleAllCameraMovement()
    {
        followTarget();
        RotateCamera();
        HandleCameraCollision();
    }
    private void followTarget()
    {
        Vector3 targetPosition = Vector3.SmoothDamp
            (transform.position, targetTransform.position, ref cameraFollowVelocity, cameraFollowSpeed);
        transform.position = targetPosition;
    }
    
    private void RotateCamera()
    {
        if (inputManager.cameraInputIsMouse)
        {
            lookAngle  += inputManager.cameraInputX * cameraLookSpeed;
            pivotAngle -= inputManager.cameraInputY * cameraPivotSpeed;
        }
        else
        {
            // Cap the step so a frame hitch doesn't whip the camera around
            // while the stick is held.
            float delta = Mathf.Min(Time.deltaTime, 0.05f);
            lookAngle  += inputManager.cameraInputX * stickLookSpeed * delta;
            pivotAngle -= inputManager.cameraInputY * stickPivotSpeed * delta;
        }

        pivotAngle = Mathf.Clamp(pivotAngle, minimumPivotAngle, maximumPivotAngle); // stop the camera flipping over

        Vector3 rotation = Vector3.zero;
        rotation.y = lookAngle;
        Quaternion targetRotation = Quaternion.Euler(rotation);
        transform.rotation = targetRotation;
        
        rotation = Vector3.zero;
        rotation.x = pivotAngle;
        
        targetRotation = Quaternion.Euler(rotation);
        cameraPivot.localRotation = targetRotation;
    }

    private void HandleCameraCollision()
    {
        targetCameraZOffset = defaultCameraZOffset;

        RaycastHit hit;
        // Cast a sphere from the pivot outward toward the camera's desired position
        if (Physics.SphereCast(cameraPivot.position, cameraCollisionRadius, -cameraPivot.forward,
            out hit, Mathf.Abs(defaultCameraZOffset), collisionLayers))
        {
            // Pull camera in to just in front of whatever was hit
            float hitDistance = Vector3.Distance(cameraPivot.position, hit.point);
            targetCameraZOffset = -(hitDistance - cameraCollisionOffset);
        }

        // Never let the camera clip into the pivot itself
        if (Mathf.Abs(targetCameraZOffset) < cameraCollisionOffset)
            targetCameraZOffset = -cameraCollisionOffset;

        // Snap in fast when hitting, ease out slowly when clearing. Rates are
        // per second and converted with Exp so the ease takes the same wall-clock
        // time at any frame rate — a flat Lerp factor would make it frame-dependent.
        Vector3 localPos = cameraTransform.localPosition;
        float smoothRate = targetCameraZOffset < localPos.z ? 60f : 10f;
        localPos.z = Mathf.Lerp(localPos.z, targetCameraZOffset,
            1f - Mathf.Exp(-smoothRate * Time.deltaTime));
        cameraTransform.localPosition = localPos;
    }

}
