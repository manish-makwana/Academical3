using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class StickyGroupMember : UIBehaviour, IPointerClickHandler, ISubmitHandler
{
    [Header("Grouping")]
    [SerializeField] private string groupId;

    [Tooltip("Mark this button as the group's default (first default wins).")]
    [SerializeField] private bool isDefaultForGroup;

    [Header("Start State")]
    [Tooltip("If ON for this group: the group starts with no selection (no default auto-pick). First setter in the scene wins for the group.")]
    [SerializeField] private bool startGroupUnselected = false;

    [Header("Flicker Guard")]
    [Tooltip("If ON, reselection uses a zero-fade handoff to avoid a visible flash on StandaloneInputModule.")]
    [SerializeField] private bool useZeroFadeReselect = true;

    private Button _button;
    public Button Button { get { return _button; } }

    protected override void Awake()
    {
        base.Awake();
        _button = GetComponent<Button>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        // Apply the per-group start-unselected flag (first setter wins)
        SelectionBus.SetStartUnselected(groupId, startGroupUnselected);

        // Normal registration
        SelectionBus.Register(groupId, this, isDefaultForGroup);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        SelectionBus.Unregister(groupId, this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsActiveAndInteractable()) return;
        SelectionBus.RequestSelect(groupId, this);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (!IsActiveAndInteractable()) return;
        SelectionBus.RequestSelect(groupId, this);
    }

    private bool IsActiveAndInteractable()
    {
        return isActiveAndEnabled && _button != null && _button.IsInteractable();
    }

    // --------- Manual controls ---------

    // Enable "start unselected" now and clear current selection.
    public void StartGroupUnselected()
    {
        SelectionBus.SetStartUnselected(groupId, true);
    }

    // Manually return to default (or clear if no valid default).
    public void ResetGroupToDefault()
    {
        SelectionBus.ResetGroupToDefault(groupId);
    }

    // ---------- Selection helpers used by bus & guard ----------

    // Called by SelectionBus to select this member with zero-fade handoff if enabled.
    internal void SelectSelfFromBus(EventSystem es)
    {
        if (es == null) return;

        if (useZeroFadeReselect && _button != null && _button.transition == Selectable.Transition.ColorTint)
        {
            var cb = _button.colors;
            float originalFade = cb.fadeDuration;
            if (originalFade > 0f)
            {
                cb.fadeDuration = 0f;
                _button.colors = cb;

                if (!es.alreadySelecting)
                    es.SetSelectedGameObject(gameObject);

                StartCoroutine(RestoreFadeEndOfFrame(originalFade));
                return;
            }
        }

        if (!es.alreadySelecting)
            es.SetSelectedGameObject(gameObject);
    }

    // ---------- Background-click guard to keep sticky selection ----------
    void LateUpdate()
    {
        // Only enforce if we are the group's current member (after user has selected us)
        if (!SelectionBus.IsCurrent(groupId, this)) return;
        if (!IsActiveAndInteractable()) return;

        var es = EventSystem.current;
        if (es == null) return;

        var cur = es.currentSelectedGameObject;

        // If another interactable Selectable is selected (e.g., another group's button), let it win.
        if (IsInteractableSelectableGO(cur)) return;

        // Only the active group may reselect this frame (avoid cross-group tug-of-war)
        if (!SelectionBus.ShouldGroupReselect(groupId)) return;

        // Re-assert selection (zero-fade optional, same helper as bus)
        SelectSelfFromBus(es);

        SelectionBus.MarkReselectedThisFrame();
    }

    private IEnumerator RestoreFadeEndOfFrame(float originalFade)
    {
        yield return new WaitForEndOfFrame();
        if (_button != null)
        {
            var cb = _button.colors;
            cb.fadeDuration = originalFade;
            _button.colors = cb;
        }
    }

    private static bool IsInteractableSelectableGO(GameObject go)
    {
        if (go == null) return false;
        var sel = go.GetComponent<Selectable>();
        return sel != null && sel.IsInteractable();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (string.IsNullOrEmpty(groupId))
            Debug.LogWarning(name + ": StickyGroupMember has empty groupId.");
        if (_button == null) _button = GetComponent<Button>();
    }
#endif
}
