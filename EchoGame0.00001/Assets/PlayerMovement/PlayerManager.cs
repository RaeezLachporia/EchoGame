using System;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    InputManager inputManager;
    CameraManager cameraManager;
    PlayerLocomotion playerLocomotion;
    PlayerBasicCombat playerCombat;

    private void Awake()
    {
        inputManager = GetComponent<InputManager>();
        cameraManager = FindObjectOfType<CameraManager>();
        playerLocomotion = GetComponent<PlayerLocomotion>();
        playerCombat = GetComponent<PlayerBasicCombat>();
    }

    private void Update()
    {
        inputManager.handleAllInput();
        playerLocomotion.tryInitiateDodge();
        playerCombat.handleAttack();
    }

    private void FixedUpdate()
    {
        playerLocomotion.handleAllMovement();
    }

    private void LateUpdate()
    {
        cameraManager.HandleAllCameraMovement();
    }
}

