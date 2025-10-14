using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class SelectionBus
{
    private class Group
    {
        public readonly HashSet<StickyGroupMember> Members = new HashSet<StickyGroupMember>();
        public StickyGroupMember Current;  // currently selected in this group
        public StickyGroupMember Default;  // first member that registered as default
    }

    private static readonly Dictionary<string, Group> _groups = new Dictionary<string, Group>();

    // Per-group flag: start unselected (no auto pick/default)
    private static readonly Dictionary<string, bool> _startUnselected = new Dictionary<string, bool>();

    // ---- Flicker coordination across groups (single owner per frame) ----
    private static string _activeGroupId;
    private static int _activeGroupFrame = -1;
    private static int _lastReselectFrame = -1;

    internal static void NotifyActiveGroup(string groupId)
    {
        _activeGroupId = groupId;
        _activeGroupFrame = Time.frameCount;
    }

    internal static bool ShouldGroupReselect(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return false;
        if (_activeGroupId == null) return true;                    // cold start
        if (!string.Equals(groupId, _activeGroupId)) return false;  // not active group
        if (_lastReselectFrame == Time.frameCount) return false;    // already reseated once this frame
        return true;
    }

    internal static void MarkReselectedThisFrame()
    {
        _lastReselectFrame = Time.frameCount;
    }

    // ---------------- Public API ----------------

    // First setter wins per group. If enabling, clear current immediately.
    public static void SetStartUnselected(string groupId, bool enable)
    {
        if (string.IsNullOrEmpty(groupId)) return;

        bool already;
        if (_startUnselected.TryGetValue(groupId, out already))
        {
            if (already != enable)
            {
                Debug.LogWarning("SelectionBus: Conflicting 'Start Unselected' for group '" + groupId + "'. Keeping first value: " + already);
            }
            return;
        }
        _startUnselected[groupId] = enable;

        Group g;
        if (_groups.TryGetValue(groupId, out g))
        {
            if (enable)
            {
                g.Current = null;
                ApplyGroupVisuals(groupId, g);
            }
            else
            {
                EnsureCurrentIsValid(groupId, g);
                ApplyGroupVisuals(groupId, g);
            }
        }
    }

    // Manually return to group's default (or clear if none/invalid)
    public static void ResetGroupToDefault(string groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return;

        Group g;
        if (!_groups.TryGetValue(groupId, out g)) return;

        if (IsValidMember(g.Default))
        {
            g.Current = g.Default;
        }
        else
        {
            g.Current = null;
        }

        // Deliberate focus action; mark active to avoid cross-group flicker
        NotifyActiveGroup(groupId);
        ApplyGroupVisuals(groupId, g);
    }

    public static StickyGroupMember GetCurrentMember(string groupId)
    {
        Group g;
        return _groups.TryGetValue(groupId, out g) ? g.Current : null;
    }

    public static bool IsCurrent(string groupId, StickyGroupMember member)
    {
        Group g;
        return _groups.TryGetValue(groupId, out g) && g.Current == member;
    }

    // ---------------- Core ----------------

    public static void Register(string groupId, StickyGroupMember member, bool isDefault)
    {
        if (string.IsNullOrEmpty(groupId) || member == null) return;

        Group g;
        if (!_groups.TryGetValue(groupId, out g))
        {
            g = new Group();
            _groups[groupId] = g;
        }

        g.Members.Add(member);

        // Record first default; only auto-select if NOT start-unselected
        if (isDefault)
        {
            if (g.Default == null)
            {
                g.Default = member;
                if (!IsStartUnselected(groupId))
                    g.Current = member;
            }
            else if (g.Default != member)
            {
                Debug.LogWarning("SelectionBus: Multiple defaults in group '" + groupId + "'. Using first: " + SafeName(g.Default));
            }
        }

        // If still no current and NOT start-unselected, fall back to first registrant
        if (g.Current == null && !IsStartUnselected(groupId))
        {
            g.Current = member;
        }

        EnsureCurrentIsValid(groupId, g);
        ApplyGroupVisuals(groupId, g);
    }

    public static void Unregister(string groupId, StickyGroupMember member)
    {
        if (string.IsNullOrEmpty(groupId) || member == null) return;

        Group g;
        if (!_groups.TryGetValue(groupId, out g)) return;

        g.Members.Remove(member);
        if (g.Default == member) g.Default = null;
        if (g.Current == member) g.Current = null;

        if (g.Members.Count == 0)
        {
            _groups.Remove(groupId);
            return;
        }

        EnsureCurrentIsValid(groupId, g);
        ApplyGroupVisuals(groupId, g);
    }

    public static void RequestSelect(string groupId, StickyGroupMember member)
    {
        if (string.IsNullOrEmpty(groupId) || member == null) return;

        Group g;
        if (!_groups.TryGetValue(groupId, out g)) return;
        if (!g.Members.Contains(member)) return;
        if (!IsSelectableActive(member.Button)) return;

        if (g.Current != member)
        {
            g.Current = member;
        }

        // User-driven selection; mark active group now
        NotifyActiveGroup(groupId);

        ApplyGroupVisuals(groupId, g);
    }

    // ---------------- Internals ----------------

    private static void ApplyGroupVisuals(string groupId, Group g)
    {
        var es = EventSystem.current;
        if (es == null) return;

        if (g.Current != null && g.Current.gameObject.activeInHierarchy)
        {
            // Inline zero-fade handoff to avoid flash on StandaloneInputModule
            var btn = g.Current.Button;
            if (btn != null && btn.transition == Selectable.Transition.ColorTint)
            {
                var cb = btn.colors;
                float originalFade = cb.fadeDuration;
                if (originalFade > 0f)
                {
                    cb.fadeDuration = 0f;
                    btn.colors = cb;

                    if (!es.alreadySelecting)
                        es.SetSelectedGameObject(g.Current.gameObject);

                    // Restore at end of frame
                    g.Current.StartCoroutine(RestoreFadeEndOfFrame(btn, originalFade));
                    return;
                }
            }

            if (!es.alreadySelecting)
                es.SetSelectedGameObject(g.Current.gameObject);
        }
        else
        {
            es.SetSelectedGameObject(null); // intentionally unselected (e.g., start-unselected or reset w/o default)
        }
    }

    private static System.Collections.IEnumerator RestoreFadeEndOfFrame(Button btn, float originalFade)
    {
        yield return new WaitForEndOfFrame();
        if (btn != null)
        {
            var cb = btn.colors;
            cb.fadeDuration = originalFade;
            btn.colors = cb;
        }
    }

    private static void EnsureCurrentIsValid(string groupId, Group g)
    {
        if (g.Current != null && IsValidMember(g.Current)) return;

        if (IsStartUnselected(groupId))
        {
            g.Current = null; // stay unselected until user clicks
            return;
        }

        if (IsValidMember(g.Default))
        {
            g.Current = g.Default;
            return;
        }

        foreach (var m in g.Members)
        {
            if (IsValidMember(m))
            {
                g.Current = m;
                return;
            }
        }

        g.Current = null;
    }

    private static bool IsStartUnselected(string groupId)
    {
        bool v;
        return _startUnselected.TryGetValue(groupId, out v) && v;
    }

    private static bool IsValidMember(StickyGroupMember m)
    {
        return m != null && IsSelectableActive(m.Button) && m.gameObject.activeInHierarchy;
    }

    private static bool IsSelectableActive(Selectable s)
    {
        if (s == null) return false;
        if (!s.gameObject.activeInHierarchy) return false;
        return s.IsInteractable();
    }

    private static string SafeName(StickyGroupMember m)
    {
        return m != null && m.gameObject != null ? m.gameObject.name : "<null>";
    }
}
