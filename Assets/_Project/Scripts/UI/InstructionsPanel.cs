using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Panel de instrucciones que aparece delante del usuario al empezar, explicando los controles.
/// Se cierra con el botón "Entendido" (rayo + gatillo) y se abre o cierra con el botón B del mando derecho.
///
/// Se crea solo al cargar la escena del apartamento (no hace falta añadirlo a mano):
/// construye su propio lienzo 3D por código.
/// </summary>
public class InstructionsPanel : MonoBehaviour
{
    const float k_Width = 1100f, k_Height = 760f, k_Padding = 40f;
    // A 1 m: lo bastante cerca para que no quede detrás de una pared o un mueble al reabrirlo.
    const float k_Distance = 1.0f;

    static readonly Color k_Background = new Color(0.08f, 0.10f, 0.13f, 0.95f);
    static readonly Color k_Text = new Color(0.93f, 0.95f, 0.97f);
    static readonly Color k_Subtle = new Color(0.65f, 0.70f, 0.76f);
    static readonly Color k_Accent = new Color(0.20f, 0.55f, 0.95f);

    const string k_MoveText =
        "<b>Caminar:</b> muévete de verdad con las gafas puestas\n" +
        "<b>Teletransporte:</b> apunta al suelo y suelta el <b>GRIP</b>\n" +
        "<b>Girar:</b> gira el cuerpo o usa el <b>joystick derecho</b>\n" +
        "<b>Maqueta:</b> botón <b>Menú</b> del mando izquierdo (otra vez: azotea); apunta y pulsa <b>GRIP</b>\n" +
        "<b>Escalera de mano (altillo):</b> acerca la mano, mantén el <b>GRIP</b> y tira hacia abajo";

    const string k_InteractText =
        "<b>Coger y mover:</b> apunta a un mueble u objeto y mantén el <b>GRIP</b>\n" +
        "<b>Acercar / girar lo que sostienes:</b> joystick de ese mando\n" +
        "<b>Cambiar tamaño:</b> GRIP con los dos mandos y separa las manos\n" +
        "<b>Pulsar botones y usar objetos:</b> <b>GATILLO</b> (p. ej. el mando de la TV)\n" +
        "<b>Lámparas, tocadiscos, interruptor:</b> apunta y pulsa el <b>GRIP</b>";

    const string k_MenusText =
        "<b>Botón X</b>  Ajustes de navegación          <b>Botón Y</b>  Muebles: color, añadir, duplicar, borrar";

    const string k_FooterText =
        "GRIP = botón lateral (dedo corazón)  ·  GATILLO = botón del dedo índice\n" +
        "Para volver a ver estas instrucciones, pulsa el <b>botón B</b> del mando derecho";

    // Altura mínima de los ojos sobre el suelo (si el seguimiento aún no está listo, la cámara está a ras de suelo).
    const float k_MinEyeHeight = 1.4f;
    const float k_TrackingTimeout = 5f;

    XROrigin m_Origin;
    Transform m_Head;
    GameObject m_Canvas;
    InputAction m_ToggleAction;
    bool m_WasPressed;
    float m_LastToggleTime = -1f;

    /// <summary>Crea el panel automáticamente en la escena del apartamento si no existe.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateIfMissing()
    {
        if (FindAnyObjectByType<InstructionsPanel>() != null || FindAnyObjectByType<PlacementArea>() == null)
            return;
        new GameObject("Instructions Panel").AddComponent<InstructionsPanel>();
    }

    void Awake()
    {
        m_Origin = FindAnyObjectByType<XROrigin>();
        m_Head = m_Origin != null ? m_Origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
        BuildCanvas();

        // Botón B del mando derecho (en el simulador: tecla 2 con el mando derecho activo).
        m_ToggleAction = new InputAction("Toggle Instructions", InputActionType.Button, "<XRController>{RightHand}/{SecondaryButton}");
    }

    // Lectura directa del botón B del mando, por si el evento del Input System no llega.
    void Update()
    {
        var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        var pressed = rightHand.isValid
                      && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out var value)
                      && value;
        if (pressed && !m_WasPressed)
            Toggle();
        m_WasPressed = pressed;
    }

    void OnEnable()
    {
        m_ToggleAction.performed += OnTogglePerformed;
        m_ToggleAction.Enable();
    }

    void OnDisable()
    {
        m_ToggleAction.performed -= OnTogglePerformed;
        m_ToggleAction.Disable();
    }

    void OnDestroy() => m_ToggleAction?.Dispose();

    IEnumerator Start()
    {
        m_Canvas.SetActive(false);

        // Se espera a que las gafas envíen la posición real de la cabeza (los ojos por encima de 1 m);
        // si no llega en unos segundos (p. ej. en el simulador), se abre igualmente a altura de ojos.
        var waited = 0f;
        while (waited < k_TrackingTimeout && EyeHeight() < 1f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(0.3f);
        Open();
    }

    /// <summary>Altura de la cabeza sobre el suelo del XR Origin.</summary>
    float EyeHeight() => m_Head == null ? 0f : m_Head.position.y - (m_Origin != null ? m_Origin.transform.position.y : 0f);

    void OnTogglePerformed(InputAction.CallbackContext context) => Toggle();

    /// <summary>
    /// Abre o cierra el panel. Las pulsaciones a menos de 0,3 s se ignoran: el Input System y la lectura
    /// directa del mando detectan la misma pulsación, y sin esto el panel se abriría y cerraría a la vez.
    /// </summary>
    void Toggle()
    {
        if (Time.unscaledTime - m_LastToggleTime < 0.3f)
            return;
        m_LastToggleTime = Time.unscaledTime;

        if (m_Canvas.activeSelf) Close();
        else Open();
        Debug.Log($"[Instrucciones] Botón B: panel {(m_Canvas.activeSelf ? "abierto" : "cerrado")}");
    }

    public void Open()
    {
        if (m_Head != null)
        {
            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            var position = m_Head.position + forward * k_Distance;
            var floorY = m_Origin != null ? m_Origin.transform.position.y : 0f;
            position.y = Mathf.Max(position.y, floorY + k_MinEyeHeight);
            m_Canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }
        m_Canvas.SetActive(true);
    }

    public void Close() => m_Canvas.SetActive(false);

    // ------------------------------------------------------------------ Construcción del lienzo

    void BuildCanvas()
    {
        m_Canvas = new GameObject("Instructions Canvas", typeof(RectTransform));
        m_Canvas.layer = LayerMask.NameToLayer("UI");
        m_Canvas.transform.SetParent(transform, false);
        m_Canvas.transform.localScale = Vector3.one * 0.001f;

        var canvas = m_Canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = m_Head != null ? m_Head.GetComponent<Camera>() : null;
        m_Canvas.AddComponent<TrackedDeviceGraphicRaycaster>();
        ((RectTransform)m_Canvas.transform).sizeDelta = new Vector2(k_Width, k_Height);
        AddImage(m_Canvas, k_Background).raycastTarget = true;

        var inner = k_Width - 2f * k_Padding;
        var column = (inner - k_Padding) / 2f;

        Text("Bienvenido a VR Room", 46, FontStyles.Bold, k_Text, TextAlignmentOptions.Left, k_Padding, -k_Padding, inner, 60f);
        Text("Un apartamento de dos plantas con azotea: muévete, reorganiza los muebles y prueba los aparatos.",
            24, FontStyles.Normal, k_Subtle, TextAlignmentOptions.Left, k_Padding, -108f, inner, 36f);

        Text("MOVERTE", 26, FontStyles.Bold, k_Accent, TextAlignmentOptions.Left, k_Padding, -168f, column, 36f);
        Text(k_MoveText, 23, FontStyles.Normal, k_Text, TextAlignmentOptions.TopLeft, k_Padding, -208f, column, 300f);

        var right = k_Padding * 2f + column;
        Text("INTERACTUAR", 26, FontStyles.Bold, k_Accent, TextAlignmentOptions.Left, right, -168f, column, 36f);
        Text(k_InteractText, 23, FontStyles.Normal, k_Text, TextAlignmentOptions.TopLeft, right, -208f, column, 300f);

        Text("MENÚS", 26, FontStyles.Bold, k_Accent, TextAlignmentOptions.Left, k_Padding, -520f, inner, 36f);
        Text(k_MenusText, 23, FontStyles.Normal, k_Text, TextAlignmentOptions.Left, k_Padding, -556f, inner, 36f);

        Text(k_FooterText, 20, FontStyles.Normal, k_Subtle, TextAlignmentOptions.Left, k_Padding, -(k_Height - k_Padding - 64f), inner - 300f, 64f);

        var button = new GameObject("Button_Entendido", typeof(RectTransform));
        button.layer = m_Canvas.layer;
        button.transform.SetParent(m_Canvas.transform, false);
        Place((RectTransform)button.transform, k_Width - k_Padding - 260f, -(k_Height - k_Padding - 70f), 260f, 70f);
        var image = AddImage(button, k_Accent);
        var uiButton = button.AddComponent<Button>();
        uiButton.targetGraphic = image;
        var colors = uiButton.colors;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        colors.colorMultiplier = 1.5f;
        uiButton.colors = colors;
        uiButton.onClick.AddListener(Close);

        var label = Text("Entendido", 30, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, 0f, 0f, 0f, 0f, button.transform);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
    }

    TextMeshProUGUI Text(string text, float size, FontStyles style, Color color, TextAlignmentOptions alignment,
        float x, float y, float width, float height, Transform parent = null)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.layer = m_Canvas.layer;
        go.transform.SetParent(parent != null ? parent : m_Canvas.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.lineSpacing = 12f;
        tmp.raycastTarget = false;
        // Si el texto no cabe en su recuadro, se reduce un poco en vez de salirse.
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = size * 0.7f;
        tmp.fontSizeMax = size;
        if (parent == null)
            Place(tmp.rectTransform, x, y, width, height);
        return tmp;
    }

    static Image AddImage(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>Coloca un rect anclado arriba a la izquierda (y negativa hacia abajo).</summary>
    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
