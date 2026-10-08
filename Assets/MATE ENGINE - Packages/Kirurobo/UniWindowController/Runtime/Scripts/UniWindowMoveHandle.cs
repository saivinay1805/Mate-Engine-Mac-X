using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_LEGACY_INPUT_MANAGER
#elif ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Kirurobo
{
    public class UniWindowMoveHandle : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerUpHandler
    {
        private UniWindowController _uniwinc;
        public bool disableOnZoomed = true;

        [Range(0f, 100f)] public float dragSmooth = 0f;

        public bool IsDragging => _isDragging;
        private bool _isDragging = false;

        private bool IsEnabled => enabled && (!disableOnZoomed || !IsZoomed);
        private bool IsZoomed => (_uniwinc && (_uniwinc.shouldFitMonitor || _uniwinc.isZoomed));

        private bool _isHitTestEnabled;
        private Vector2 _grabOffset;

        private Animator _avatarAnimator;
        private bool _hasSitParam;
        private static readonly int IsWindowSitHash = Animator.StringToHash("isWindowSit");
        private static readonly int IsDraggingHash = Animator.StringToHash("isDragging");

        private Vector2 _dragTarget;
        private Vector2 _dragVel;
        private const float MaxSmoothTime = 0.35f;

        private bool _wasHardwareLeftDown = false;

        // Callbacks hooked by higher-level gameplay/avatar controllers (in Assembly-CSharp)
        public static UniWindowMoveHandle Instance { get; private set; }
        public static Func<bool> IsMovementBlockedCallback;
        public static Func<Vector2, Vector2> ModifyDragTargetCallback;
        public static Action OnDragStarted;
        public static Action OnDragEnded;

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("MacSystem")]
        private static extern int MacSys_GetPressedMouseButtons();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
#endif

        private static bool IsHardwareLeftPressed()
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            try { return (MacSys_GetPressedMouseButtons() & 1) != 0; }
            catch { return Input.GetMouseButton(0); }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try { return (GetAsyncKeyState(0x01) & 0x8000) != 0; }
            catch { return Input.GetMouseButton(0); }
#else
            return Input.GetMouseButton(0);
#endif
        }

        private static bool IsModifierPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#elif ENABLE_INPUT_SYSTEM
            return (Keyboard.current != null) && (
                Keyboard.current[Key.LeftShift].isPressed || Keyboard.current[Key.RightShift].isPressed
                || Keyboard.current[Key.LeftCtrl].isPressed || Keyboard.current[Key.RightCtrl].isPressed
                || Keyboard.current[Key.LeftAlt].isPressed || Keyboard.current[Key.RightAlt].isPressed);
#else
            return false;
#endif
        }

        private static bool IsFullScreen()
        {
#if !UNITY_EDITOR
            return Screen.fullScreen;
#else
            return false;
#endif
        }

        private bool IsMovementBlocked()
        {
            try
            {
                if (IsMovementBlockedCallback != null && IsMovementBlockedCallback()) return true;
            }
            catch { }
            return false;
        }

        private bool IsPointerOverOtherUI()
        {
            try
            {
                if (EventSystem.current == null) return false;
                var pointerData = new PointerEventData(EventSystem.current)
                {
                    position = Input.mousePosition
                };
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, results);
                for (int i = 0; i < results.Count; i++)
                {
                    var go = results[i].gameObject;
                    if (go != null && go != gameObject && !go.transform.IsChildOf(transform))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            Instance = this;
            _uniwinc = GameObject.FindAnyObjectByType<UniWindowController>();
            if (_uniwinc) _isHitTestEnabled = _uniwinc.isHitTestEnabled;
            RefreshAnimator();
        }

        public void ResetGrabOffset()
        {
            if (_uniwinc)
            {
                _grabOffset = _uniwinc.windowPosition - _uniwinc.cursorPosition;
            }
        }

        public void StartDirectDrag()
        {
            if (!IsEnabled) return;
            RefreshAnimator();
            if (!_uniwinc) return;

            _grabOffset = _uniwinc.windowPosition - _uniwinc.cursorPosition;
            if (!_isDragging)
            {
                _isHitTestEnabled = _uniwinc.isHitTestEnabled;
                _uniwinc.isHitTestEnabled = false;
                _uniwinc.isClickThrough = false;
            }
            _isDragging = true;
            _dragVel = Vector2.zero;
            _dragTarget = _uniwinc.windowPosition;

            if (_avatarAnimator != null && HasParam(_avatarAnimator, IsDraggingHash))
            {
                _avatarAnimator.SetBool(IsDraggingHash, true);
            }

            try { OnDragStarted?.Invoke(); } catch { }
        }

        private void UpdateDragTarget()
        {
            if (!_uniwinc) return;
            if (_avatarAnimator == null || !_hasSitParam) RefreshAnimator();

            bool lockY = _avatarAnimator && _hasSitParam && _avatarAnimator.GetBool(IsWindowSitHash);
            Vector2 next = _uniwinc.cursorPosition + _grabOffset;
            if (lockY) next.y = _uniwinc.windowPosition.y;

            if (ModifyDragTargetCallback != null)
            {
                next = ModifyDragTargetCallback(next);
            }

            _dragTarget = next;
        }

        public void EndDirectDrag()
        {
            if (_isDragging && _uniwinc)
            {
                _uniwinc.isHitTestEnabled = _isHitTestEnabled;
            }
            _isDragging = false;
            _dragVel = Vector2.zero;

            if (_avatarAnimator != null && HasParam(_avatarAnimator, IsDraggingHash))
            {
                _avatarAnimator.SetBool(IsDraggingHash, false);
            }

            try { OnDragEnded?.Invoke(); } catch { }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_isDragging) return;
            if (!IsEnabled || IsFullScreen() || IsMovementBlocked() || IsModifierPressed()) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;

            StartDirectDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_uniwinc || !_isDragging) return;
            if (!IsEnabled || IsFullScreen() || IsMovementBlocked() || IsModifierPressed()) { EndDirectDrag(); return; }
            if (eventData.button != PointerEventData.InputButton.Left) return;

            UpdateDragTarget();
        }

        public void OnEndDrag(PointerEventData eventData) { EndDirectDrag(); }
        public void OnPointerUp(PointerEventData eventData) { EndDirectDrag(); }

        void Update()
        {
            if (!_uniwinc) return;

            bool hwLeftDown = IsHardwareLeftPressed();
            bool justPressed = hwLeftDown && !_wasHardwareLeftDown;

            // Direct drag check:
            // When hardware left mouse is pressed down on the character, start drag immediately on frame 0!
            // No prior activation click required (matches Desktop Mate UX).
            if (justPressed && !_isDragging)
            {
                if (IsEnabled && !IsFullScreen() && !IsMovementBlocked() && !IsModifierPressed() && !IsPointerOverOtherUI())
                {
                    if (_uniwinc.isOnObject)
                    {
                        StartDirectDrag();
                    }
                }
            }

            // Dragging update loop
            if (_isDragging)
            {
                if (!hwLeftDown || !IsEnabled || IsFullScreen() || IsMovementBlocked())
                {
                    EndDirectDrag();
                }
                else
                {
                    UpdateDragTarget();

                    float t = Mathf.Clamp01(dragSmooth * 0.01f) * MaxSmoothTime;
                    if (t <= 0f) _uniwinc.windowPosition = _dragTarget;
                    else _uniwinc.windowPosition = Vector2.SmoothDamp(_uniwinc.windowPosition, _dragTarget, ref _dragVel, t);
                }
            }

            _wasHardwareLeftDown = hwLeftDown;
        }

        private void RefreshAnimator()
        {
            Animator best = GetComponentInParent<Animator>();
            if (best == null || !HasParam(best, IsWindowSitHash))
            {
                var all = GameObject.FindObjectsOfType<Animator>();
                for (int i = 0; i < all.Length; i++)
                {
                    var a = all[i];
                    if (HasParam(a, IsWindowSitHash) && a.GetBool(IsWindowSitHash)) { best = a; break; }
                }
                if (best == null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var a = all[i];
                        if (HasParam(a, IsWindowSitHash)) { best = a; break; }
                    }
                }
                if (best == null && all.Length > 0) best = all[0];
            }
            _avatarAnimator = best;
            _hasSitParam = _avatarAnimator && HasParam(_avatarAnimator, IsWindowSitHash);
        }

        private static bool HasParam(Animator a, int hash)
        {
            if (!a) return false;
            var ps = a.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].nameHash == hash) return true;
            return false;
        }
    }
}