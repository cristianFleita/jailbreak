using UnityEngine;

/// <summary>
/// Controlador de movimiento del personaje con CharacterController.
/// Incluye movimiento, rotación suave, gravedad y compatibilidad con SitInteraction.
/// Requiere: CharacterController, Animator (opcional)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // CONFIGURACIÓN
    // ─────────────────────────────────────────────────────────

    [Header("Movimiento")]
    [Tooltip("Velocidad de caminar")]
    public float walkSpeed = 4f;

    [Tooltip("Velocidad de correr (Shift)")]
    public float runSpeed = 7f;

    [Tooltip("Qué tan rápido el personaje rota hacia la dirección de movimiento")]
    public float rotationSpeed = 10f;

    [Header("Gravedad y Salto")]
    public float gravity = -20f;
    public float jumpHeight = 1.2f;

    [Header("Detección de suelo")]
    public float groundCheckDistance = 0.15f;
    public LayerMask groundLayer;

    [Header("Cámara")]
    [Tooltip("Transform de la cámara principal (para orientar el movimiento)")]
    public Transform cameraTransform;

    [Header("Animaciones (opcional)")]
    public Animator animator;

    // IDs de parámetros del Animator (para performance)
    private static readonly int AnimSpeed     = Animator.StringToHash("speed");
    private static readonly int AnimIsRunning = Animator.StringToHash("isRunning");
    private static readonly int AnimGrounded  = Animator.StringToHash("isGrounded");

    // ─────────────────────────────────────────────────────────
    // REFERENCIAS INTERNAS
    // ─────────────────────────────────────────────────────────

    private CharacterController cc;
    private SitInteraction sitInteraction;  // Para deshabilitar movimiento al sentarse

    private Vector3 velocity;              // Velocidad acumulada (gravedad)
    private bool isGrounded;

    // ─────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        sitInteraction = GetComponent<SitInteraction>();

        // Si no se asignó cámara manualmente, usar la cámara principal
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        
        
        // No mover si el personaje está sentado
        if (sitInteraction != null && sitInteraction.IsSitting)
            return;

        CheckGround();
        HandleMovement();
        ApplyGravity();
        HandleJump();
    }

    // ─────────────────────────────────────────────────────────
    // MOVIMIENTO
    // ─────────────────────────────────────────────────────────

    void CheckGround()
    {
        // Raycast corto hacia abajo para detectar el suelo
        isGrounded = Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            groundCheckDistance + 0.1f,
            groundLayer
        );

        // Resetear velocidad vertical al tocar el suelo
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        // animator?.SetBool(AnimGrounded, isGrounded);
    }

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A / D
        float v = Input.GetAxisRaw("Vertical");   // W / S

        Vector3 input = new Vector3(h, 0f, v).normalized;

        if (input.magnitude < 0.1f)
        {
            // Sin input → idle
            animator?.SetFloat(AnimSpeed, 0f, 0.1f, Time.deltaTime);
            animator?.SetBool(AnimIsRunning, false);
            return;
        }

        // Orientar el input relativo a la cámara
        Vector3 moveDir = GetCameraRelativeDirection(input);

        // Velocidad actual
        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float speed = isRunning ? runSpeed : walkSpeed;

        // Mover el CharacterController
        cc.Move(moveDir * speed * Time.deltaTime);

        // Rotar el personaje suavemente hacia donde se mueve
        RotateTowards(moveDir);

        // Actualizar animaciones
        float normalizedSpeed = isRunning ? 1f : 0.5f;
        animator?.SetFloat(AnimSpeed, normalizedSpeed, 0.1f, Time.deltaTime);
        animator?.SetBool(AnimIsRunning, isRunning);
    }

    void ApplyGravity()
    {
        velocity.y += gravity * Time.deltaTime;
        cc.Move(new Vector3(0f, velocity.y, 0f) * Time.deltaTime);
    }

    void HandleJump()
    {
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            // v = sqrt(2 * |g| * h)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    // ─────────────────────────────────────────────────────────
    // UTILIDADES
    // ─────────────────────────────────────────────────────────

    /// Convierte el input de teclado a dirección relativa a la cámara
    Vector3 GetCameraRelativeDirection(Vector3 input)
    {
        if (cameraTransform == null)
            return input;

        // Forward y Right de la cámara, pero en el plano horizontal (sin inclinación)
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight   = cameraTransform.right;
        camForward.y = 0f;
        camRight.y   = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * input.z + camRight * input.x).normalized;
    }

    /// Rota el personaje suavemente hacia la dirección de movimiento
    void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    // ─────────────────────────────────────────────────────────
    // GIZMOS (ayuda visual en el editor)
    // ─────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        // Muestra el rayo de detección de suelo
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        Gizmos.DrawLine(origin, origin + Vector3.down * (groundCheckDistance + 0.1f));
    }
}