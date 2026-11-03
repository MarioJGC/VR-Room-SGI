using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Navegación propia: maqueta en miniatura del apartamento (World-in-Miniature) que se lleva
/// en la mano izquierda. El botón de menú del mando izquierdo pasa por tres estados:
/// interior (planta baja y altillo, sin techo) → azotea (con el techo) → oculta.
/// Al apuntar a la maqueta con el rayo derecho aparece un marcador amarillo en el punto de destino;
/// al pulsar el grip, el usuario se teletransporta a ese punto del apartamento real
/// (planta baja, escalera, altillo o azotea). Un marcador azul indica dónde está el usuario.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class WorldInMiniature : MonoBehaviour
{
    [Tooltip("Raíz del apartamento a escala real.")]
    [SerializeField] Transform m_WorldRoot;

    [Tooltip("Raíz de la copia en miniatura: sus coordenadas locales coinciden con las de m_WorldRoot.")]
    [SerializeField] Transform m_ModelRoot;

    [SerializeField] Transform m_PlayerMarker;
    [SerializeField] Transform m_TargetMarker;
    [SerializeField] TeleportationProvider m_Teleport;
    [SerializeField] XROrigin m_Origin;

    [SerializeField] bool m_StartVisible;
    [SerializeField] AudioClip m_ToggleClip;
    [SerializeField] AudioClip m_TeleportClip;

    XRSimpleInteractable m_Interactable;
    XRRayInteractor m_HoveringRay;
    InputAction m_ToggleAction;
    bool m_Visible;
    GameObject m_RoofLevel;

    /// <summary>Se lanza al mostrar u ocultar la maqueta (para sincronizar el panel de ajustes).</summary>
    public event System.Action<bool> VisibilityChanged;

    public bool Visible
    {
        get => m_Visible;
        set
        {
            m_Visible = value;
            m_ModelRoot.gameObject.SetActive(value);
            if (!value)
            {
                ShowRoof = false;
                if (m_TargetMarker != null)
                    m_TargetMarker.gameObject.SetActive(false);
            }
            VisibilityChanged?.Invoke(value);
        }
    }

    /// <summary>Muestra el nivel de la azotea (techo) en la maqueta; tapa el interior.</summary>
    public bool ShowRoof
    {
        get => m_RoofLevel != null && m_RoofLevel.activeSelf;
        set
        {
            if (m_RoofLevel != null)
                m_RoofLevel.SetActive(value);
        }
    }

    void Awake()
    {
        m_Interactable = GetComponent<XRSimpleInteractable>();
        if (m_Teleport == null) m_Teleport = FindAnyObjectByType<TeleportationProvider>();
        if (m_Origin == null) m_Origin = FindAnyObjectByType<XROrigin>();

        // Botón de menú del mando izquierdo (en el simulador: tecla M con el mando izquierdo activo).
        m_ToggleAction = new InputAction("Toggle Miniature", InputActionType.Button);
        m_ToggleAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
        m_ToggleAction.AddBinding("<XRController>{LeftHand}/menu");
    }

    void OnEnable()
    {
        m_Interactable.hoverEntered.AddListener(OnHoverEntered);
        m_Interactable.hoverExited.AddListener(OnHoverExited);
        m_Interactable.selectEntered.AddListener(OnSelectEntered);
        m_ToggleAction.performed += OnTogglePerformed;
        m_ToggleAction.Enable();
    }

    void OnDisable()
    {
        m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
        m_Interactable.hoverExited.RemoveListener(OnHoverExited);
        m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
        m_ToggleAction.performed -= OnTogglePerformed;
        m_ToggleAction.Disable();
    }

    void OnDestroy() => m_ToggleAction?.Dispose();

    void Start()
    {
        BuildRoofLevel();

        // La maqueta va en la mano: que sus colliders no choquen con el cuerpo del jugador.
        if (m_Origin != null && m_Origin.TryGetComponent<CharacterController>(out var body))
        {
            foreach (var c in m_ModelRoot.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(body, c);
        }

        // Se oculta en Start (no en Awake) para que el interactuable registre antes los colliders de la maqueta.
        // El nivel de la azotea empieza oculto (se activa con el botón de menú).
        Visible = m_StartVisible;
        ShowRoof = false;
    }

    /// <summary>Botón de menú: oculta → interior → azotea → oculta.</summary>
    void OnTogglePerformed(InputAction.CallbackContext context)
    {
        if (!Visible)
        {
            Visible = true;
            ShowRoof = false;
        }
        else if (!ShowRoof && m_RoofLevel != null)
        {
            ShowRoof = true;
        }
        else
        {
            Visible = false;
        }
        PlayClip(m_ToggleClip);
    }

    /// <summary>
    /// Crea en la maqueta el nivel de la azotea a partir del techo real (losas, tarima, muro y barandillas)
    /// y una zona a la que apuntar para teletransportarse a la azotea.
    /// Se rehace con cubos nuevos en vez de copiar los originales porque, en el build, el static batching
    /// fusiona las mallas del techo y una copia directa no se vería bien.
    /// </summary>
    void BuildRoofLevel()
    {
        var structure = m_WorldRoot != null ? m_WorldRoot.Find("Structure") : null;
        var roof = structure != null ? structure.Find("Roof") : null;
        if (roof == null)
            return;

        m_RoofLevel = new GameObject("Roof Level");
        m_RoofLevel.transform.SetParent(m_ModelRoot, false);

        foreach (Transform piece in roof)
        {
            if (!piece.TryGetComponent<MeshRenderer>(out var source))
                continue;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.name = piece.name;
            cube.transform.SetParent(m_RoofLevel.transform, false);
            cube.transform.localPosition = m_WorldRoot.InverseTransformPoint(piece.position);
            cube.transform.localRotation = Quaternion.Inverse(m_WorldRoot.rotation) * piece.rotation;
            cube.transform.localScale = piece.lossyScale;
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Zona a la que se apunta en la maqueta para ir a la azotea (copia de su zona de teletransporte).
        var roofTeleport = structure.Find("Roof_Teleport");
        if (roofTeleport != null)
        {
            var target = new GameObject("Roof Target");
            target.transform.SetParent(m_RoofLevel.transform, false);
            target.transform.localPosition = m_WorldRoot.InverseTransformPoint(roofTeleport.position);
            foreach (var box in roofTeleport.GetComponents<BoxCollider>())
            {
                var copy = target.AddComponent<BoxCollider>();
                copy.center = box.center;
                copy.size = box.size;
                m_Interactable.colliders.Add(copy);
            }

            // Se vuelve a registrar el interactuable para que tenga en cuenta los colliders nuevos.
            if (m_Interactable.enabled)
            {
                m_Interactable.enabled = false;
                m_Interactable.enabled = true;
            }
        }
    }

    void Update()
    {
        if (!m_Visible)
            return;

        UpdatePlayerMarker();
        UpdateTargetMarker();
    }

    void UpdatePlayerMarker()
    {
        if (m_PlayerMarker == null || m_Origin == null)
            return;

        var head = m_Origin.Camera.transform;
        var feet = new Vector3(head.position.x, m_Origin.transform.position.y, head.position.z);
        m_PlayerMarker.position = WorldToModel(feet);

        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.001f)
        {
            var localForward = m_WorldRoot.InverseTransformDirection(forward);
            m_PlayerMarker.rotation = m_ModelRoot.rotation * Quaternion.LookRotation(localForward, Vector3.up);
        }
    }

    void UpdateTargetMarker()
    {
        if (m_TargetMarker == null)
            return;

        RaycastHit hit = default;
        var showTarget = m_HoveringRay != null
                         && m_HoveringRay.TryGetCurrent3DRaycastHit(out hit)
                         && hit.collider.transform.IsChildOf(m_ModelRoot)
                         && TryGetDestination(hit.point, out _);
        m_TargetMarker.gameObject.SetActive(showTarget);
        if (showTarget)
            m_TargetMarker.position = hit.point;
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (args.interactorObject is XRRayInteractor ray)
            m_HoveringRay = ray;
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (ReferenceEquals(args.interactorObject, m_HoveringRay))
            m_HoveringRay = null;
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (m_Teleport == null || !(args.interactorObject is XRRayInteractor ray))
            return;
        if (!ray.TryGetCurrent3DRaycastHit(out var hit) || !TryGetDestination(hit.point, out var destination))
            return;

        m_Teleport.QueueTeleportRequest(new TeleportRequest
        {
            destinationPosition = destination,
            destinationRotation = Quaternion.identity,
            matchOrientation = MatchOrientation.None,
            requestTime = Time.time,
        });
        HoverFeedback.SendHaptic(args.interactorObject, 0.5f, 0.1f);
        PlayClip(m_TeleportClip);
    }

    /// <summary>
    /// Convierte un punto de la maqueta en un punto del apartamento real y busca debajo
    /// una zona de teletransporte (suelo, escalera o altillo).
    /// </summary>
    bool TryGetDestination(Vector3 modelPoint, out Vector3 destination)
    {
        var world = ModelToWorld(modelPoint);
        var hits = Physics.RaycastAll(world + Vector3.up * 0.5f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform))
                continue;
            if (hit.collider.GetComponentInParent<TeleportationArea>() != null)
            {
                destination = hit.point;
                return true;
            }
        }

        destination = default;
        return false;
    }

    Vector3 ModelToWorld(Vector3 modelPoint) => m_WorldRoot.TransformPoint(m_ModelRoot.InverseTransformPoint(modelPoint));

    Vector3 WorldToModel(Vector3 worldPoint) => m_ModelRoot.TransformPoint(m_WorldRoot.InverseTransformPoint(worldPoint));

    void PlayClip(AudioClip clip)
    {
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, transform.position, 0.5f);
    }
}
