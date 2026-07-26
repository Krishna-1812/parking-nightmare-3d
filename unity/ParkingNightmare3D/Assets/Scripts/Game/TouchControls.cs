using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// On-screen driving controls: a rotary steering wheel and gas / brake / handbrake
    /// pads, ported from the web build's touch layer (src/n3_b.js setupTouch).
    ///
    /// The mapping is deliberately identical to the reference — the wheel travels ±120°
    /// and divides down to the same −1..1 the keyboard produces, and SteerAnalog stays
    /// false so the §4 swept-path attack still applies. A phone that steered differently
    /// would silently invalidate every par time and star threshold in the game.
    ///
    /// INPUT SOURCE. The pads are UI Toolkit elements but their input does NOT come from
    /// UI Toolkit pointer events. It is read straight off the Touchscreen device and
    /// hit-tested against each element's rect. UI Toolkit's runtime panel routes a single
    /// primary pointer, which is fine for a menu and useless here: holding gas while
    /// turning the wheel is two simultaneous fingers, and it is the normal case rather
    /// than an edge case. The elements stay purely visual, and this owns the arbitration.
    /// </summary>
    public sealed class TouchControls
    {
        /// <summary>Full lock at 120° of wheel rotation, as in the reference.</summary>
        const float MaxWheel = 120f * Mathf.Deg2Rad;

        /// <summary>
        /// Per-frame spring-back factor when the wheel is released. Copied from the
        /// reference, where it runs off requestAnimationFrame — so it is per rendered
        /// frame, not per second, and both builds target 60fps. Frame-rate independence
        /// would be more correct in the abstract and would make the two feel different,
        /// which matters more.
        /// </summary>
        const float SpringPerFrame = 0.76f;

        readonly VisualElement _root, _wheel, _gfx, _padGas, _padBrake, _padHb;

        float _wheelAngle;
        int _wheelPointer = -1;
        float _grabAngle, _grabBase;
        bool _gas, _brake, _hb;

        public bool Enabled { get; private set; }

        /// <summary>True on any frame a finger went down anywhere — the brief's "tap to start".</summary>
        public bool TappedThisFrame { get; private set; }

        public TouchControls(VisualElement root)
        {
            _root     = root.Q<VisualElement>("touch");
            _wheel    = _root?.Q<VisualElement>("wheel");
            _gfx      = _root?.Q<VisualElement>("wheel-gfx");
            _padGas   = _root?.Q<VisualElement>("pad-gas");
            _padBrake = _root?.Q<VisualElement>("pad-brake");
            _padHb    = _root?.Q<VisualElement>("pad-hb");

            // Application.isMobilePlatform rather than a touchscreen check: a Windows
            // laptop with a touch panel should still get the keyboard HUD.
            Enabled = _root != null && Application.isMobilePlatform;
            _root?.EnableInClassList("hidden", !Enabled);
        }

        /// <summary>Force the controls on regardless of platform, for editor testing.</summary>
        public void ForceEnable()
        {
            if (_root == null) return;
            Enabled = true;
            _root.EnableInClassList("hidden", false);
        }

        public VehicleInput Read() => new VehicleInput
        {
            Steer = Mathf.Clamp(_wheelAngle / MaxWheel, -1f, 1f),
            Throttle = (_gas ? 1 : 0) + (_brake ? -1 : 0),
            Handbrake = _hb,
            SteerAnalog = false,   // the on-screen wheel keeps the swept-path feel (§4)
        };

        /// <summary>Poll the touchscreen and update wheel angle and pad states.</summary>
        public void Update()
        {
            TappedThisFrame = false;
            if (!Enabled || _root == null) return;

            Span<Ptr> pointers = stackalloc Ptr[10];
            int n = Gather(ref pointers);

            _gas = _brake = _hb = false;
            bool wheelHeld = false;

            for (int i = 0; i < n; i++)
            {
                var p = pointers[i];
                if (p.Began) TappedThisFrame = true;

                // The wheel keeps its finger for the whole drag, even once it has
                // travelled outside the circle — releasing at the edge of a turn is
                // exactly when the player is least able to aim.
                if (p.Id == _wheelPointer)
                {
                    wheelHeld = true;
                    DragWheel(p.Panel);
                    continue;
                }

                if (_wheelPointer < 0 && p.Began && Hit(_wheel, p.Panel))
                {
                    _wheelPointer = p.Id;
                    _grabAngle = AngleFrom(_wheel, p.Panel);
                    _grabBase = _wheelAngle;
                    wheelHeld = true;
                    continue;
                }

                if (Hit(_padGas, p.Panel)) _gas = true;
                else if (Hit(_padBrake, p.Panel)) _brake = true;
                else if (Hit(_padHb, p.Panel)) _hb = true;
            }

            if (!wheelHeld)
            {
                _wheelPointer = -1;
                _wheelAngle *= SpringPerFrame;
                if (Mathf.Abs(_wheelAngle) < 0.01f) _wheelAngle = 0f;
            }

            Paint(wheelHeld);
        }

        void DragWheel(Vector2 panelPos)
        {
            float d = (float)MathX.AngNorm(AngleFrom(_wheel, panelPos) - _grabAngle);
            _wheelAngle = Mathf.Clamp(_grabBase + d, -MaxWheel, MaxWheel);
        }

        void Paint(bool wheelHeld)
        {
            if (_gfx != null) _gfx.style.rotate = new StyleRotate(new Rotate(_wheelAngle * Mathf.Rad2Deg));
            _wheel?.EnableInClassList("down", wheelHeld);
            _padGas?.EnableInClassList("down", _gas);
            _padBrake?.EnableInClassList("down", _brake);
            _padHb?.EnableInClassList("down", _hb);
        }

        struct Ptr
        {
            public int Id;
            public Vector2 Panel;
            public bool Began;
        }

        /// <summary>
        /// Collect this frame's active pointers in panel coordinates. Mouse counts as one
        /// pointer so the controls can be exercised in the editor without a device.
        /// </summary>
        int Gather(ref Span<Ptr> into)
        {
            int n = 0;
            var panel = _root.panel;

            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    if (n >= into.Length) break;
                    // wasPressedThisFrame as well as isPressed: a tap short enough to go
                    // down and up inside one frame is already released by the time this
                    // polls, and skipping it would swallow the press entirely. Rare from a
                    // finger, routine from injected input, and "tap to start" silently
                    // failing is the worst possible way to lose it.
                    if (!t.press.isPressed && !t.press.wasPressedThisFrame) continue;
                    into[n++] = new Ptr
                    {
                        Id = t.touchId.ReadValue(),
                        Panel = ToPanel(panel, t.position.ReadValue()),
                        Began = t.press.wasPressedThisFrame,
                    };
                }
            }

            var mouse = Mouse.current;
            if (n == 0 && mouse != null && mouse.leftButton.isPressed)
            {
                into[n++] = new Ptr
                {
                    Id = -2,
                    Panel = ToPanel(panel, mouse.position.ReadValue()),
                    Began = mouse.leftButton.wasPressedThisFrame,
                };
            }

            return n;
        }

        /// <summary>
        /// Screen position to panel position, flipping Y on the way.
        ///
        /// The Input System reports screen space with the origin at the BOTTOM-left; UI
        /// Toolkit panel space has it at the TOP-left, and RuntimePanelUtils.ScreenToPanel
        /// does not flip on your behalf — it only undoes the panel's scaling.
        ///
        /// Verified on device, because the symptom is otherwise baffling: every pad
        /// hit-tested mirrored vertically, so pressing GO where it is drawn did nothing at
        /// all and pressing the empty sky directly above it pressed GO. Nothing about the
        /// rendering looks wrong, which is what makes this worth a comment.
        /// </summary>
        static Vector2 ToPanel(IPanel panel, Vector2 screenPos)
        {
            screenPos.y = Screen.height - screenPos.y;
            return RuntimePanelUtils.ScreenToPanel(panel, screenPos);
        }

        static bool Hit(VisualElement e, Vector2 panelPos)
            => e != null && e.worldBound.Contains(panelPos);

        static float AngleFrom(VisualElement e, Vector2 panelPos)
        {
            var c = e.worldBound.center;
            return Mathf.Atan2(panelPos.y - c.y, panelPos.x - c.x);
        }
    }
}
