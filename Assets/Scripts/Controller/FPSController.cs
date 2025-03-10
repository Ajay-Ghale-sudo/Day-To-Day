using System;
using System.Collections;
using System.Collections.Generic;
using Audio;
using Component;
using UI;
using UnityEngine;
using UnityEngine.Serialization;
using Events;

namespace Controller
{
    /// <summary>
    ///    <para>First Person Controller</para>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(InteractorComponent))]
    public class FPSController : MonoBehaviour
    {
        /// <summary>
        ///  <para>Event to be invoked when the player is spawned</para>
        /// </summary>
        public static Action<FPSController> OnPlayerSpawned; 
        /// <summary>
        ///   <para>Walking speed of the player</para>
        /// </summary>
        [Header("Movement")] [SerializeField] private float walkSpeed = 4.0f;

        ///<summary>
        /// <para>Running speed of the player.</para>
        /// </summary>
        [Header("Movement")] [SerializeField] private float runSpeed = 8.0f;
        
        /// <summary>
        ///  <para>Standing height of the player</para>
        /// </summary>
        [SerializeField] private float standingHeight = 2.0f;

        /// <summary>
        /// <para>Sensitivity of the mouse</para>
        /// </summary>
        [Header("Mouse")] [SerializeField] private float mouseSensitivity = 2.0f;
        
        /// <summary>
        /// The maximum angle the player can look up or down
        /// </summary>
        [SerializeField] private float maxLookAngle = 90.0f;
        
        /// <summary>
        /// Invert the Y axis
        /// </summary>
        [SerializeField] private bool invertY = false;

        /// <summary>
        /// The distance to check if the player is grounded
        /// </summary>
        [Header("Ground Check")] [SerializeField]
        private LayerMask groundMask;

        /// <summary>
        /// The distance to check if the player is grounded
        /// </summary>
        [SerializeField] private float groundCheckDistance = 0.4f;
        
        /// <summary>
        /// The distance between walking steps
        /// </summary>
        [SerializeField] private float stride_Walk = 1.6f;

        /// <summary>
        /// The distance between running steps
        /// </summary>
        [SerializeField] private float stride_Run = 2.0f;
        
        /// <summary>
        /// The current step distance the player has taken
        /// </summary>
        private float _currentStepTime = 0f;

        /// <summary>
        /// The time between headbobs. calculated based on speed and stride length
        /// </summary>
        private float _timeBetweenSteps => 1/(_currentSpeed / (CheckSprint() ? stride_Run : stride_Walk));
        
        /// <summary>
        /// How much the camera should bob
        /// </summary>
        [Header("HeadBob")]
        [SerializeField] private float headBobIntensity = .1f;

        /// <summary>
        /// How much the camera should bob while sprinting
        /// </summary>
        [SerializeField] private float headBobSprintIntensity = 0.15f;

        /// <summary>
        /// The head bob routine
        /// </summary>
        private Coroutine _headBobRoutine;
        
        /// <summary>
        /// The initial Y position of the camera
        /// </summary>
        private float _initialYPos;

        /// <summary>
        /// The character controller component
        /// </summary>
        private CharacterController _characterController;
        
        /// <summary>
        /// The interactor component
        /// </summary>
        private InteractorComponent _interactorComponent;
        
        /// <summary>
        /// The player camera
        /// </summary>
        private Camera _playerCamera;

        // Movement
        /// <summary>
        /// The movement direction of the player
        /// </summary>
        private Vector3 _moveDirection;
        
        /// <summary>
        /// The velocity of the player
        /// </summary>
        private Vector3 _velocity;
        
        /// <summary>
        /// The current speed of the player
        /// </summary>
        private float _currentSpeed;

        // Mouse Look
        /// <summary>
        /// The rotation of the player on the X axis
        /// </summary>
        private float _rotationX = 0f;
        
        /// <summary>
        /// The rotation of the player on the Y axis
        /// </summary>
        private float _rotationY = 0f;
        
        public bool MovementEnabled { get; set; } = false;

        private void Awake()
        {
            OnPlayerSpawned?.Invoke(this);
        }

        /// <summary>
        /// The keybinding to start sprinting.
        /// </summary>
        [SerializeField] 
        private KeyCode sprintKey;

        [Header("Audio")]
        /// <summary>
        /// Audio event for footsteps
        /// </summary>
        public AudioEvent FootStepAudioEvent;

        /// <summary>
        /// Audio event for footsteps
        /// </summary>
        public Transform FootStepAudioPosition;

        // Start is called before the first frame update
        private void Start()
        {
            UIManager.Instance.OnNodeTransitionEnd += Init;
            Init();
            MovementEnabled = false;
        }

        public void Init()
        {
            _characterController = GetComponent<CharacterController>();
            _playerCamera = GetComponentInChildren<Camera>();
            _interactorComponent = GetComponent<InteractorComponent>();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            _currentStepTime = 0f;
            _headBobRoutine = null;
            _initialYPos = _playerCamera.transform.localPosition.y;
            _rotationX = transform.localEulerAngles.x;
            _rotationY = transform.localEulerAngles.y;
            _playerCamera.transform.localRotation = Quaternion.Euler(_rotationX, 
                _playerCamera.transform.localRotation.y, _playerCamera.transform.localRotation.z);
            transform.rotation = Quaternion.Euler(transform.rotation.x, _rotationY, transform.rotation.z);
            
            UIManager.Instance.OnEnterUIMode += DisableInteraction;
            UIManager.Instance.OnExitUIMode += EnableInteraction;
        }

        private void OnEnable()
        {
            AudioManager.Instance.EnableDefaultListener(false);
        }

        private void OnDisable()
        {
            AudioManager.Instance.EnableDefaultListener(true);
        }

        private void OnDestroy()
        {
            UIManager.Instance.OnEnterUIMode -= DisableInteraction;
            UIManager.Instance.OnExitUIMode -= EnableInteraction;
            UIManager.Instance.OnNodeTransitionEnd -= Init;
        }

        /// <summary>
        /// <para>Enable the <see cref="InteractorComponent"/></para>
        /// </summary>
        private void EnableInteraction()
        {
            if (!_interactorComponent)
            {
                _interactorComponent = GetComponent<InteractorComponent>();
            }
            _interactorComponent.enabled = true;
        }
        
        /// <summary>
        /// <para>Disable the <see cref="InteractorComponent"/></para>
        /// </summary>
        private void DisableInteraction()
        {
            if (!_interactorComponent)
            {
                _interactorComponent = GetComponent<InteractorComponent>();
            }
            _interactorComponent.enabled = false;
        }

        // Update is called once per frame
        private void Update()
        {
            if (!MovementEnabled) return;
            HandleInput();
            HandleMovement();
            HandleMouseLook();
            CheckStep();
        }

        /// <summary>
        ///  <para>Handles the player input</para>
        /// </summary>
        private void HandleInput()
        {
            // Movement
            var horizontal = Input.GetAxisRaw("Horizontal");
            var vertical = Input.GetAxisRaw("Vertical");
            _moveDirection = new Vector3(horizontal, 0, vertical).normalized;
        }

        /// <summary>
        /// <para>Check if the player is taking a step</para>
        /// </summary>
        private void CheckStep()
        {
            // If we're grounded and our velocity is greater than 0, check if we're over the step distance
            var horizontalVelocity = new Vector3(_velocity.x, 0, _velocity.z).magnitude;
            if (!_characterController.isGrounded || horizontalVelocity <= 0)
            {
                _currentStepTime = 0f;
                return;
            }

            _currentStepTime += Time.deltaTime;

            if (_headBobRoutine == null && _currentStepTime >= _timeBetweenSteps)
            {
                Step();
            }
        }

        /// <summary>
        /// <para>Check to see if the sprint key is being held down.</para>
        /// </summary>
        private bool CheckSprint()
        {
            if (Input.GetKey(sprintKey))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Function that managed the Headbob coroutine
        /// </summary>
        private void Step()
        {
            if (_headBobRoutine != null)
            {
                StopCoroutine(_headBobRoutine);
                _headBobRoutine = null;
            }

            _currentStepTime = 0f;

            FootStepAudioEvent?.Invoke(FootStepAudioPosition ? FootStepAudioPosition.gameObject : gameObject);
            _headBobRoutine = StartCoroutine(HeadBob());
        }

        /// <summary>
        /// Headbob coroutine
        /// </summary>
        /// <returns></returns>
        private IEnumerator HeadBob()
        {
            var time = 0f;
            while (time < _timeBetweenSteps)
            {
                time += Time.deltaTime;
                var t = time / _timeBetweenSteps;
                var bobAmount = Mathf.Sin(t * Mathf.PI) * (CheckSprint() ? headBobSprintIntensity : headBobIntensity);
                _playerCamera.transform.localPosition =  
                    new Vector3(
                        _playerCamera.transform.localPosition.x, 
                        _initialYPos - bobAmount,
                        _playerCamera.transform.localPosition.z);
                yield return null;
            }

            _headBobRoutine = null;
        }

        /// <summary>
        /// <para>Handles the player movement</para>
        /// </summary>
        private void HandleMovement()
        {
            _currentSpeed = CheckSprint() ? runSpeed : walkSpeed;
            _velocity = transform.TransformDirection(_moveDirection) * _currentSpeed;
            
            _velocity.y += Physics.gravity.y;
            _characterController.Move(_velocity * Time.deltaTime);
        }

        /// <summary>
        /// <para>Handles the player mouse look</para>
        /// </summary>
        private void HandleMouseLook()
        {
            var mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            var mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            
            if (mouseX == 0 && mouseY == 0) return;

            _rotationX -= mouseY;
            _rotationX = Mathf.Clamp(_rotationX, -maxLookAngle, maxLookAngle);

            _rotationY += mouseX;

            _playerCamera.transform.localRotation = Quaternion.Euler(_rotationX, 
                _playerCamera.transform.localRotation.y, _playerCamera.transform.localRotation.z);
            transform.rotation = Quaternion.Euler(transform.rotation.x, _rotationY, transform.rotation.z);
        }
    }
}