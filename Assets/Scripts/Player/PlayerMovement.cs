using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header(" Togles")]
    public bool canMove = true;
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canRun = true;
    [SerializeField] private bool canCrouch = true;
    [SerializeField] private bool canHeadBob = true;
    [SerializeField] private bool hasFoostepSound = true;
    [SerializeField] private bool infiniteStamina = false;

    [Header("Movement Settings")]
    [SerializeField] private float currentSpeed;
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private MovementState currentMovementState;

    [Header("Stamina Settings")]
    [SerializeField] private float stamina = 5f;
    [SerializeField] private float staminaRecoverSpeed = 3f;
    [SerializeField] private float staminaTiredRecoverSpeed = 1f;
    [SerializeField] private bool isTired = false;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchTransitionSpeed = 5f;
    [SerializeField] private LayerMask crouchBlockLayerMask;
    [SerializeField] private float crouchHeight = 1.3f;
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float headCamOffset = 1.1f;

    [Header("Jump Settings")]
    [SerializeField] private bool isGrounded;
    [SerializeField] private float groundCheckDistance = 0.7f;
    [SerializeField] private float gravity = -14f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundMask;

    [Header("Head Bobbing Settings")]
    [SerializeField] private float walkBobbingSpeed = 12f;
    [SerializeField] private float walkBobbingAmount = 0.025f;
    [SerializeField] private float runBobbingSpeed = 16f;
    [SerializeField] private float runBobbingAmount = 0.05f;
    [SerializeField] private float crouchBobbingSpeed = 6f;
    [SerializeField] private float crouchBobbingAmount = 0.0125f;

    [Header("Head Bobbing Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private float walkingFootstepInterval = 0.7f;
    [SerializeField] private float runningFootstepInterval = 0.035f;



    //private variables
    private Vector3 groundNormal;
    private float jumpBufferTimer;
    private float footstepTimer;
    private int currentFootstepIndex;
    private float coyoteTimer;
    private float maxStamina;
    private CharacterController controller;
    private Vector3 velocity;
    private Transform playerCamera;
    private float currentHeight;
    private Vector3 currentCameraPos;
    private float headBobTimer;
    private const float InvalidTimerValue = -10f;
    public enum MovementState { Walking, Running, Crouching, Idle }


    void Start()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>().transform;
        maxStamina = stamina;

        currentHeight = standingHeight;
        currentCameraPos = new Vector3(0, standingHeight - headCamOffset, 0);
    }

    void FixedUpdate()
    {
        GroundCheck();
    }

    void Update()
    {
        // reset vertical velocity if grounded
        if (isGrounded && velocity.y < 0)
        {
            //magical value just to make sure player is on ground
            velocity.y = -1f;
        }

        if (!canMove) return;

        // TODO: Change to unity's input system later
        // Input
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        HandleMovementStances();

        HandleFootstepsSound(x, z);

        HandleCrouch();

        HandleHeadBob(x, z);

        HandleStamina();

        HandleJumpBuffer();

        HandleCoyoteTime();

        // Movement
        Vector3 move = (transform.right * x + transform.forward * z).normalized;
        if (isGrounded)
        {
            move = Vector3.ProjectOnPlane(move, groundNormal);
            AdjustSpeedBasedOnState();
        }

        controller.Move(currentSpeed * Time.deltaTime * move);

        HandleJump();

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void AdjustSpeedBasedOnState()
    {
        currentSpeed = currentMovementState switch
        {
            MovementState.Crouching => crouchSpeed,
            MovementState.Running => runSpeed,
            _ => walkSpeed
        };
    }

    private void HandleFootstepsSound(float horizontal, float vertical)
    {
        if (!isGrounded || !hasFoostepSound) return;

        float interval = currentMovementState == MovementState.Running ? runningFootstepInterval : walkingFootstepInterval;

        if (Mathf.Abs(horizontal) == 0 && Mathf.Abs(vertical) == 0)
        {
            // Reset footstep timer if not moving
            footstepTimer = interval / 2;
            return;
        }

        footstepTimer += Time.deltaTime;

        if (footstepTimer >= interval)
        {
            footstepTimer = 0f;
            PlayFootstepSound();
        }
    }

    private void PlayFootstepSound()
    {
        int index = GetNextFootstepIndexInOrder();
        audioSource.clip = footstepClips[index];
        audioSource.Play();
    }

    int GetNextFootstepIndexInOrder()
    {
        int index = currentFootstepIndex;
        currentFootstepIndex = (currentFootstepIndex + 1) % footstepClips.Length;
        return index;
    }


    private void HandleHeadBob(float horizontal, float vertical)
    {
        if (!canHeadBob || !isGrounded) return;

        // Determine the speed and amount of head bobbing based on the player's movement speed
        float bobbingSpeed = currentMovementState == MovementState.Crouching ? crouchBobbingSpeed : currentMovementState == MovementState.Running ? runBobbingSpeed : walkBobbingSpeed;
        float bobbingAmount = currentMovementState == MovementState.Crouching ? crouchBobbingAmount : currentMovementState == MovementState.Running ? runBobbingAmount : walkBobbingAmount;

        if (Mathf.Abs(horizontal) == 0 && Mathf.Abs(vertical) == 0)
        {
            // Reset wave slice if player is not moving
            headBobTimer = 0.0f;
            //TODO: add a smooth transition to the idle position
        }
        else
        {
            // Accumulate time based on movement
            headBobTimer += Time.deltaTime * bobbingSpeed;
        }

        // Calculate wave slice using the head bob timer
        float waveSlice = Mathf.Sin(headBobTimer);

        // Calculate head bobbing offset
        Vector3 headBobbingOffset = new Vector3(0, waveSlice * bobbingAmount, 0);

        // Apply head bobbing offset to the camera
        playerCamera.localPosition = currentCameraPos + headBobbingOffset;
    }

    private void GroundCheck()
    {
        // Multi-raycast setup
        RaycastHit hit;
        Vector3[] raycastPositions = new Vector3[]
        {
            groundCheck.position,                                   // Center
            groundCheck.position + new Vector3(controller.radius, 0, 0),   // Right
            groundCheck.position - new Vector3(controller.radius, 0, 0),   // Left
            groundCheck.position + new Vector3(0, 0, controller.radius),   // Forward
            groundCheck.position - new Vector3(0, 0, controller.radius),   // Backward
        };

        isGrounded = false; // Reset grounded status

        // Draw and check each raycast position
        foreach (var raycastPosition in raycastPositions)
        {
            // Draw the ray in the scene view (green if grounded, red if not)
            Color rayColor = Physics.Raycast(raycastPosition, Vector3.down, out hit, groundCheckDistance, groundMask) ? Color.green : Color.red;
            Debug.DrawRay(raycastPosition, Vector3.down * groundCheckDistance, rayColor);

            if (raycastPosition == groundCheck.position) groundNormal = hit.normal;

            // Check if this ray hits the ground
            if (rayColor == Color.green)
            {
                isGrounded = true;
                break; // As soon as one raycast hits the ground, break out
            }
        }
    }

    private void HandleJump()
    {
        if (!canJump || !canMove || currentMovementState == MovementState.Crouching) return;

        if (jumpBufferTimer >= 0 && coyoteTimer > 0)
        {
            // Reset timers
            coyoteTimer = InvalidTimerValue;
            jumpBufferTimer = InvalidTimerValue;

            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    private void HandleCrouch()
    {
        if (!canMove || !isGrounded) return;

        // Target height and camera position based on whether the player is crouching
        float targetHeight = (currentMovementState == MovementState.Crouching ? crouchHeight : standingHeight) + controller.skinWidth;
        Vector3 targetCameraPos = new Vector3(0, targetHeight - headCamOffset, 0);

        // Check if there is something above the player before allowing them to stand up
        if (currentMovementState == MovementState.Crouching && IsObstacleAbove())
        {
            // If an obstacle is detected, prevent standing up
            targetHeight = crouchHeight;
            targetCameraPos = new Vector3(0, crouchHeight - headCamOffset, 0);
            currentMovementState = MovementState.Crouching;
        }

        // Smoothly interpolate between current height and target height
        currentHeight = Mathf.Lerp(currentHeight, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        currentCameraPos = Vector3.Lerp(currentCameraPos, targetCameraPos, crouchTransitionSpeed * Time.deltaTime);

        // Apply the new height and camera position
        controller.height = currentHeight;
        controller.center = new Vector3(0, (currentHeight - standingHeight) / 2, 0);
        playerCamera.localPosition = currentCameraPos;
    }

    private bool IsObstacleAbove()
    {
        Vector3 start = transform.position + Vector3.up * crouchHeight / 2;  // Starting point of the cast (player's current height)
        Vector3 end = transform.position + Vector3.up * (standingHeight / 2);  // Ending point of the cast (target height)

        // Perform a capsule cast to check for obstacles
        return Physics.CheckCapsule(start, end, controller.radius, crouchBlockLayerMask);
    }

    private void HandleMovementStances()
    {
        // TODO: Change to unity's input system later
        if (canRun && isGrounded && !isTired && Input.GetKey(KeyCode.LeftShift))
        {
            currentMovementState = MovementState.Running;
        }
        // TODO: Change to unity's input system later
        else if (canCrouch && isGrounded && Input.GetKey(KeyCode.LeftControl))
        {
            currentMovementState = MovementState.Crouching;
        }
        else
        {
            currentMovementState = MovementState.Walking;
        }
    }

    private void HandleCoyoteTime()
    {
        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    private void HandleJumpBuffer()
    {
        // TODO: Change to unity's input system later
        if (Input.GetButtonDown("Jump"))
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer -= Time.deltaTime;
        }
    }

    private void HandleStamina()
    {
        if (infiniteStamina) return;

        if (currentMovementState == MovementState.Running && !isTired)
        {
            // Se está correndo e não está cansado, consome stamina
            stamina -= Time.deltaTime;

            if (stamina <= 0)
            {
                // Se a stamina acabou, entra no estado de cansaço
                stamina = 0;
                isTired = true;
            }
        }
        else
        {
            // Se não está correndo ou está cansado, começa a recuperar stamina
            if (isTired)
            {
                // Se está cansado, recupera stamina mais lentamente
                stamina += staminaTiredRecoverSpeed * Time.deltaTime;

                if (stamina >= maxStamina)
                {
                    // Quando a stamina é totalmente recuperada, sai do estado de cansaço
                    stamina = maxStamina;
                    isTired = false;
                }
            }
            else
            {
                // Se não está cansado, recupera stamina em velocidade normal
                if (stamina < maxStamina)
                {
                    stamina += staminaRecoverSpeed * Time.deltaTime;
                }
            }
        }
    }
}
