using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Kirurobo;

public class AvatarHideHandler : MonoBehaviour
{
    public static AvatarHideHandler Instance { get; private set; }
    public static bool IsHiding => Instance != null && Instance.snappedSide != Side.None;

    public enum Side { None, Left, Right }

    [Header("Edge Snapping Configuration")]
    [Tooltip("Distance in points to screen bezel to trigger magnetic snap.")]
    public float snapThresholdPoints = 30f;

    [Tooltip("Distance in points cursor must pull inward to break snap.")]
    public float unsnapThresholdPoints = 18f;

    [Tooltip("Grace period in seconds after snapping before an unsnap can trigger.")]
    public float unsnapGraceTime = 0.15f;

    [Tooltip("Cooldown in seconds after unsnapping during which re-snapping is blocked.")]
    public float unsnapCooldownSeconds = 0.8f;

    [Tooltip("Offset in points character is recessed behind the bezel for flush peeking.")]
    public float edgeRecessPoints = 22f;

    [Tooltip("Keep window on topmost layer while hiding at screen edge.")]
    public bool keepTopmostWhileSnapped = true;

    [Header("Multi-Monitor Adjacency")]
    public int adjacencyTolerancePx = 8;
    public int adjacencyMinVerticalOverlapPx = 32;

    [Header("Current State")]
    public Side snappedSide = Side.None;

    private Animator animator;
    private AvatarAnimatorController controller;
    private UniWindowController uniwinc;
    private Camera cam;

    private Transform leftHand;
    private Transform rightHand;

    private bool _hasHideLeftParam = false;
    private bool _hasHideRightParam = false;
    private float snappedAt = 0f;
    private float unsnapCooldownUntil = 0f;
    private MonitorBounds snappedMonitor;

    public struct MonitorBounds
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
        public int width => right - left;
        public int height => bottom - top;
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Instance = this;
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        uniwinc = UniWindowController.current;
        cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();

        RefreshBones();
    }

    void OnEnable()
    {
        Instance = this;
        RefreshBones();

        UniWindowMoveHandle.ModifyDragTargetCallback -= OnModifyDragTarget;
        UniWindowMoveHandle.ModifyDragTargetCallback += OnModifyDragTarget;

        UniWindowMoveHandle.OnDragStarted -= HandleDragStarted;
        UniWindowMoveHandle.OnDragStarted += HandleDragStarted;

        UniWindowMoveHandle.OnDragEnded -= HandleDragEnded;
        UniWindowMoveHandle.OnDragEnded += HandleDragEnded;
    }

    void OnDisable()
    {
        UniWindowMoveHandle.ModifyDragTargetCallback -= OnModifyDragTarget;
        UniWindowMoveHandle.OnDragStarted -= HandleDragStarted;
        UniWindowMoveHandle.OnDragEnded -= HandleDragEnded;

        SetHide(false, false);
        snappedSide = Side.None;
    }

    void OnDestroy()
    {
        UniWindowMoveHandle.ModifyDragTargetCallback -= OnModifyDragTarget;
        UniWindowMoveHandle.OnDragStarted -= HandleDragStarted;
        UniWindowMoveHandle.OnDragEnded -= HandleDragEnded;

        if (Instance == this) Instance = null;
    }

    private void RefreshBones()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            _hasHideLeftParam = HasParam(animator, Animator.StringToHash("HideLeft"));
            _hasHideRightParam = HasParam(animator, Animator.StringToHash("HideRight"));

            if (animator.isHuman)
            {
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }
    }

    private static bool HasParam(Animator a, int hash)
    {
        if (a == null) return false;
        foreach (var p in a.parameters)
        {
            if (p.nameHash == hash) return true;
        }
        return false;
    }

    public void HandleDragStarted()
    {
    }

    public void HandleDragEnded()
    {
        if (snappedSide != Side.None && keepTopmostWhileSnapped)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            MacWindowHelper.SetTopMost(true);
#endif
        }
    }

    public void SnapTo(Side side, MonitorBounds mon)
    {
        snappedSide = side;
        snappedMonitor = mon;
        snappedAt = Time.unscaledTime;
        SetHide(side == Side.Left, side == Side.Right);

        if (keepTopmostWhileSnapped)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            MacWindowHelper.SetTopMost(true);
#endif
        }
    }

    public void Unsnap()
    {
        Side oldSide = snappedSide;
        snappedSide = Side.None;
        unsnapCooldownUntil = Time.unscaledTime + unsnapCooldownSeconds;
        SetHide(false, false);

        if (uniwinc != null)
        {
            GetHandOffsets(out float leftWinX, out float rightWinX);
            float winW = (uniwinc.windowSize.x > 0f) ? uniwinc.windowSize.x : 1536f;
            float defaultHalf = winW * 0.5f;

            float anchorWinX = (oldSide == Side.Left) ? leftWinX : rightWinX;
            float poseCompensationX = anchorWinX - defaultHalf;

            uniwinc.windowPosition = new Vector2(uniwinc.windowPosition.x + poseCompensationX, uniwinc.windowPosition.y);
            UniWindowMoveHandle.Instance?.ResetGrabOffset();
        }
    }

    private void SetHide(bool left, bool right)
    {
        if (animator == null) RefreshBones();
        if (animator == null) return;
        if (_hasHideLeftParam) animator.SetBool("HideLeft", left);
        if (_hasHideRightParam) animator.SetBool("HideRight", right);
    }

    private void GetHandOffsets(out float leftWinX, out float rightWinX)
    {
        float winW = (uniwinc != null && uniwinc.windowSize.x > 0f) ? uniwinc.windowSize.x : 1536f;
        float defaultHalf = winW * 0.5f;
        leftWinX = defaultHalf;
        rightWinX = defaultHalf;

        if (cam == null) cam = Camera.main ? Camera.main : FindAnyObjectByType<Camera>();
        if (animator == null) RefreshBones();
        if (cam == null || animator == null) return;

        if ((leftHand == null || rightHand == null) && animator.isHuman)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        float pw = (cam.pixelWidth > 0) ? (float)cam.pixelWidth : (float)Screen.width;
        if (pw <= 0f) pw = 1920f;

        float lx = -1f;
        if (leftHand != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(leftHand.position);
            if (sp.z > 0.01f) lx = (sp.x / pw) * winW;
        }

        float rx = -1f;
        if (rightHand != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(rightHand.position);
            if (sp.z > 0.01f) rx = (sp.x / pw) * winW;
        }

        if (lx >= 0f && rx >= 0f)
        {
            leftWinX = Mathf.Min(lx, rx);
            rightWinX = Mathf.Max(lx, rx);
        }
        else if (lx >= 0f)
        {
            leftWinX = lx;
            rightWinX = lx;
        }
        else if (rx >= 0f)
        {
            leftWinX = rx;
            rightWinX = rx;
        }
        else
        {
            Vector3 sp = cam.WorldToScreenPoint(transform.position);
            float cx = (sp.z > 0.01f) ? (sp.x / pw) * winW : defaultHalf;
            leftWinX = cx - (winW * 0.1f);
            rightWinX = cx + (winW * 0.1f);
        }
    }

    public Vector2 OnModifyDragTarget(Vector2 proposedWinPos)
    {
        if (uniwinc == null) uniwinc = UniWindowController.current;
        if (uniwinc == null) return proposedWinPos;

        List<MonitorBounds> allMons = GetAllMonitors();
        if (allMons.Count == 0) return proposedWinPos;

        float cursorDeskX = uniwinc.cursorPosition.x;
        MonitorBounds mon = (snappedSide != Side.None && snappedMonitor.width > 0) ? snappedMonitor : FindBestMonitor(cursorDeskX, allMons);
        GetAllowedEdges(mon, allMons, out bool allowLeft, out bool allowRight);

        GetHandOffsets(out float leftWinX, out float rightWinX);

        if (snappedSide == Side.None)
        {
            // If recently unsnapped, allow free dragging without re-snapping!
            if (Time.unscaledTime < unsnapCooldownUntil)
            {
                return proposedWinPos;
            }

            float proposedLeftHandDesk = proposedWinPos.x + leftWinX + edgeRecessPoints;
            float proposedRightHandDesk = proposedWinPos.x + rightWinX - edgeRecessPoints;

            bool nearLeft = allowLeft && (proposedLeftHandDesk <= mon.left + snapThresholdPoints || cursorDeskX <= mon.left + 25f);
            bool nearRight = allowRight && (proposedRightHandDesk >= mon.right - snapThresholdPoints || cursorDeskX >= mon.right - 25f);

            if (nearLeft)
            {
                SnapTo(Side.Left, mon);
                proposedWinPos.x = mon.left - leftWinX - edgeRecessPoints;
            }
            else if (nearRight)
            {
                SnapTo(Side.Right, mon);
                proposedWinPos.x = mon.right - rightWinX + edgeRecessPoints;
            }
        }
        else
        {
            // Currently snapped: check for unsnap (pulling away from edge)
            if (Time.unscaledTime >= snappedAt + unsnapGraceTime)
            {
                bool pullAwayLeft = (snappedSide == Side.Left) && (cursorDeskX - mon.left > unsnapThresholdPoints);
                bool pullAwayRight = (snappedSide == Side.Right) && (mon.right - cursorDeskX > unsnapThresholdPoints);

                if (pullAwayLeft || pullAwayRight)
                {
                    Unsnap();
                    return (uniwinc != null) ? uniwinc.windowPosition : proposedWinPos;
                }
            }

            // While dragged while snapped: lock X to edge, allow Y to follow cursor
            float targetWinX = (snappedSide == Side.Left)
                ? (mon.left - leftWinX - edgeRecessPoints)
                : (mon.right - rightWinX + edgeRecessPoints);

            proposedWinPos.x = targetWinX;
        }

        return proposedWinPos;
    }

    void LateUpdate()
    {
        if (snappedSide == Side.None || IsCurrentlyDragging()) return;
        if (uniwinc == null) return;

        MonitorBounds mon = snappedMonitor;
        if (mon.width <= 0)
        {
            List<MonitorBounds> allMons = GetAllMonitors();
            if (allMons.Count == 0) return;
            float charDeskX = uniwinc.windowPosition.x + (uniwinc.windowSize.x * 0.5f);
            mon = FindBestMonitor(charDeskX, allMons);
            snappedMonitor = mon;
        }

        GetHandOffsets(out float leftWinX, out float rightWinX);

        float targetWinX = (snappedSide == Side.Left)
            ? (mon.left - leftWinX - edgeRecessPoints)
            : (mon.right - rightWinX + edgeRecessPoints);

        if (Mathf.Abs(uniwinc.windowPosition.x - targetWinX) > 1.0f)
        {
            uniwinc.windowPosition = new Vector2(targetWinX, uniwinc.windowPosition.y);
        }
    }

    private bool IsCurrentlyDragging()
    {
        if (UniWindowMoveHandle.Instance != null && UniWindowMoveHandle.Instance.IsDragging) return true;
        if (controller != null && controller.isDragging) return true;
        return false;
    }

    private MonitorBounds FindBestMonitor(float charX, List<MonitorBounds> monitors)
    {
        if (monitors.Count == 1) return monitors[0];

        int bestIndex = 0;
        float minDistance = float.MaxValue;

        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorBounds m = monitors[i];
            if (charX >= m.left && charX < m.right)
            {
                return m;
            }

            float dist = Mathf.Min(Mathf.Abs(charX - m.left), Mathf.Abs(charX - m.right));
            if (dist < minDistance)
            {
                minDistance = dist;
                bestIndex = i;
            }
        }

        return monitors[bestIndex];
    }

    private void GetAllowedEdges(MonitorBounds cur, List<MonitorBounds> all, out bool allowLeft, out bool allowRight)
    {
        if (all.Count <= 1)
        {
            allowLeft = true;
            allowRight = true;
            return;
        }

        bool hasLeftNeighbor = false;
        bool hasRightNeighbor = false;

        for (int i = 0; i < all.Count; i++)
        {
            MonitorBounds other = all[i];
            if (other.left == cur.left && other.right == cur.right) continue;

            int overlap = VerticalOverlap(cur, other);
            if (overlap < adjacencyMinVerticalOverlapPx) continue;

            if (Mathf.Abs(other.right - cur.left) <= adjacencyTolerancePx) hasLeftNeighbor = true;
            if (Mathf.Abs(cur.right - other.left) <= adjacencyTolerancePx) hasRightNeighbor = true;
        }

        allowLeft = !hasLeftNeighbor;
        allowRight = !hasRightNeighbor;
    }

    private int VerticalOverlap(MonitorBounds a, MonitorBounds b)
    {
        int top = Math.Max(a.top, b.top);
        int bottom = Math.Min(a.bottom, b.bottom);
        return Math.Max(0, bottom - top);
    }

    private List<MonitorBounds> GetAllMonitors()
    {
        List<MonitorBounds> list = new List<MonitorBounds>();
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        var rects = MacWindowHelper.GetMonitors();
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            list.Add(new MonitorBounds { left = r.x, top = r.y, right = r.x + r.width, bottom = r.y + r.height });
        }
#elif UNITY_STANDALONE_WIN
        GCHandle gch = GCHandle.Alloc(list);
        IntPtr data = GCHandle.ToIntPtr(gch);
        try
        {
            MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                GCHandle h = GCHandle.FromIntPtr(dwData);
                List<MonitorBounds> target = (List<MonitorBounds>)h.Target;
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    target.Add(new MonitorBounds {
                        left = mi.rcMonitor.Left,
                        top = mi.rcMonitor.Top,
                        right = mi.rcMonitor.Right,
                        bottom = mi.rcMonitor.Bottom
                    });
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, data);
        }
        finally
        {
            gch.Free();
        }
#else
        float sw = (uniwinc != null && uniwinc.windowSize.x > 0) ? 1920f : Screen.width;
        float sh = (uniwinc != null && uniwinc.windowSize.y > 0) ? 1080f : Screen.height;
        list.Add(new MonitorBounds { left = 0, top = 0, right = (int)sw, bottom = (int)sh });
#endif
        if (list.Count == 0)
        {
            list.Add(new MonitorBounds { left = 0, top = 0, right = 1920, bottom = 1080 });
        }
        return list;
    }

#if UNITY_STANDALONE_WIN
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
#endif
}
