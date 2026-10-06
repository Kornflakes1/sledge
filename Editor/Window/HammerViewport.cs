using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	public enum ViewType
	{
		Perspective,
		Top,
		Front,
		Side,
	}

	public enum ViewShading
	{
		Lit,
		Fullbright,
		Wire,
	}

	partial class HammerWindow
	{
		static readonly HashSet<KeyCode> _flyKeys = new();
		static bool _flying;

		/// <summary>
		/// True while the right mouse button is held in the 3D view (fly mode).
		/// </summary>
		public static bool Flying => _flying;

		/// <summary>
		/// Fly keys are also Hammer shortcuts (W, E...), and Unity's shortcut manager eats their
		/// key-down before the window sees it, so the shortcuts forward them here while flying.
		/// </summary>
		public static void FlyKeyDown( KeyCode key ) => _flyKeys.Add( key );

		/// <summary>
		/// One of the four viewports: camera placement, navigation and grid.
		/// </summary>
		[Serializable]
		sealed class Viewport
		{
			public ViewType Type;
			public Vector3 Pivot;
			public float Yaw = 45;
			public float Pitch = 30;
			public float Distance = 6;
			public float OrthoSize = 4;
			public ViewShading Shading;
			[SerializeField] bool _shadingSet;

			public bool Wireframe => Shading == ViewShading.Wire;
			/// <summary>
			/// Fly speed in Hammer units (inches) per second.
			/// </summary>
			public float FlySpeedUnits = 640;

			[NonSerialized] Camera _camera;
			[NonSerialized] double _lastFlyTime;
			[NonSerialized] double _speedShownUntil;

			public Viewport( ViewType type )
			{
				Type = type;
				Shading = type != ViewType.Perspective ? ViewShading.Wire : ViewShading.Lit;
				_shadingSet = true;
			}

			public bool IsOrtho => Type != ViewType.Perspective;

			/// <summary>
			/// The view's dropdown (type and shading), top right like Hammer.
			/// </summary>
			public Rect LabelRect( Rect view ) => new( view.xMax - 6 - 22 - 2 - 120, view.y + 5, 120, 18 );

			/// <summary>
			/// The layout button next to the view dropdown.
			/// </summary>
			public Rect LayoutRect( Rect view ) => new( view.xMax - 6 - 22, view.y + 5, 22, 18 );

			public string DisplayName => Type switch
			{
				ViewType.Top => "Top",
				ViewType.Front => "Front",
				ViewType.Side => "Side",
				_ => Shading switch { ViewShading.Wire => "3D Wire", ViewShading.Fullbright => "Fullbright", _ => "Lit" },
			};

			public Quaternion Rotation => Type switch
			{
				// Hammer's axes in Unity terms: s&box X = Unity +Z, Y = Unity -X, Z = Unity +Y
				ViewType.Top => Quaternion.LookRotation( Vector3.down, Vector3.left ),     // x right, y up
				ViewType.Front => Quaternion.LookRotation( Vector3.back, Vector3.up ),    // y right, z up
				ViewType.Side => Quaternion.LookRotation( Vector3.left, Vector3.up ),     // x right, z up
				_ => Quaternion.Euler( Pitch, Yaw, 0 ),
			};

			public Vector3 CameraPosition => IsOrtho
				? Pivot - Rotation * Vector3.forward * 1000
				: Pivot - Rotation * Vector3.forward * Distance;

			public Camera EnsureCamera()
			{
				if ( _camera != null )
					return _camera;

				var go = EditorUtility.CreateGameObjectWithHideFlags( $"Hammer {Type} Camera", HideFlags.HideAndDontSave, typeof( Camera ) );
				_camera = go.GetComponent<Camera>();
				_camera.enabled = false;
				_camera.cameraType = CameraType.SceneView;
				return _camera;
			}

			[NonSerialized] RenderTexture _target;

			/// <summary>
			/// The texture this view last rendered into (for tests).
			/// </summary>
			[NonSerialized] public RenderTexture LastRender;

			public RenderTexture EnsureTarget( Vector2 pixelSize )
			{
				int w = Mathf.Max( 1, Mathf.RoundToInt( pixelSize.x ) );
				int h = Mathf.Max( 1, Mathf.RoundToInt( pixelSize.y ) );

				if ( _target == null || _target.width != w || _target.height != h )
				{
					if ( _target != null )
					{
						_target.Release();
						DestroyImmediate( _target );
					}

					_target = new RenderTexture( w, h, 24, RenderTextureFormat.ARGB32 ) { hideFlags = HideFlags.HideAndDontSave, name = $"Hammer {Type} View" };
					_target.Create();
				}

				return _target;
			}

			public void DestroyCamera()
			{
				if ( _camera != null )
					DestroyImmediate( _camera.gameObject );
				_camera = null;

				if ( _target != null )
				{
					_target.Release();
					DestroyImmediate( _target );
				}
				_target = null;
			}

			public void ApplyToCamera( Rect rect )
			{
				if ( FlySpeedUnits <= 0 ) FlySpeedUnits = 640; // views saved before the speed setting existed

				// Views saved before shading modes: 2D views were wireframe
				if ( !_shadingSet )
				{
					Shading = IsOrtho ? ViewShading.Wire : ViewShading.Lit;
					_shadingSet = true;
				}

				var c = EnsureCamera();
				c.orthographic = IsOrtho;
				c.aspect = rect.width / Mathf.Max( rect.height, 1 );
				c.transform.SetPositionAndRotation( CameraPosition, Rotation );

				if ( IsOrtho )
				{
					c.orthographicSize = OrthoSize;
					c.nearClipPlane = 0.01f;
					c.farClipPlane = 4000;
					c.clearFlags = CameraClearFlags.SolidColor;
					c.backgroundColor = Color.black;
				}
				else
				{
					c.fieldOfView = 60;
					// Keep the near/far ratio sane so overlays don't z-fight with the surfaces under them
					c.nearClipPlane = Mathf.Clamp( Distance * 0.01f, 0.02f, 1 );
					c.farClipPlane = HammerSettings.BackplaneDistance * SourceSpace.UnitScale;
					c.clearFlags = Shading == ViewShading.Lit ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
					c.backgroundColor = Color.black;
				}
			}

			public void Frame( Bounds bounds )
			{
				Pivot = bounds.center;
				var radius = Mathf.Max( bounds.extents.magnitude, 0.25f );

				if ( IsOrtho )
					OrthoSize = radius * 1.2f;
				else
					Distance = radius / Mathf.Sin( 30 * Mathf.Deg2Rad ) * 1.1f;
			}

			/// <summary>
			/// Camera controls, after Source 2 Hammer:
			/// 3D: hold right mouse to look, with WASD (Q/E down/up) to fly, Shift to go faster, wheel
			/// to change speed while held; wheel alone pushes forward/back; middle mouse pans;
			/// Alt+left orbits, Alt+right dollies, Alt+middle pans.
			/// 2D: wheel zooms at the cursor (Ctrl+wheel zooms all 2D views), right/middle mouse pans.
			/// Returns true if the event was used for navigation.
			/// </summary>
			public bool HandleNavigation( Event e, Rect rect, HammerWindow window )
			{
				var id = GUIUtility.GetControlID( NavigationHash, FocusType.Passive );
				_shiftHeld = e.shift;
				_ctrlHeld = e.control || e.command;

				if ( FlyMode && FlyModeGUI( e, rect, window ) )
					return true;

				switch ( e.GetTypeForControl( id ) )
				{
					case EventType.ScrollWheel when _looking:
					{
						// Right mouse + wheel changes fly speed
						FlySpeedUnits = Mathf.Clamp( FlySpeedUnits * (e.delta.y > 0 ? 1 / 1.25f : 1.25f), 16, HammerSettings.ForwardSpeedMax );
						_speedShownUntil = EditorApplication.timeSinceStartup + 1.5;
						e.Use();
						window.Repaint();
						return true;
					}

					case EventType.ScrollWheel:
					{
						if ( IsOrtho )
						{
							var factor = e.delta.y > 0 ? 1.2f : 1 / 1.2f;
							ZoomOrthoAt( e.mousePosition, rect, factor );

							// Ctrl+wheel keeps all the 2D views at the same zoom, like Hammer
							if ( e.control || e.command )
								window.MatchOrthoZoom( this );
						}
						else
						{
							if ( e.control || e.shift ) return false; // the tool uses modified wheel (paint brush, bevel)

							// Each notch pushes the camera a fraction of a second's flight
							MoveForward( -Mathf.Sign( e.delta.y ) * FlySpeedUnits * SourceSpace.UnitScale * 0.15f );
						}

						e.Use();
						window.Repaint();
						return true;
					}

					case EventType.MouseDown:
					{
						if ( GUIUtility.hotControl != 0 )
							return false;

						// Shift/Ctrl + right mouse are the material pick/paint controls
						if ( e.button == 1 && (e.shift || e.control || e.command) )
							return false;

						var mode = Nav.None;

						if ( IsOrtho )
						{
							// Right drag (or Alt+left / Alt+middle) pans; a plain middle drag is the lasso
							if ( e.button == 1 || (e.alt && e.button == 0 && e.clickCount < 2) || (e.alt && e.button == 2) ) mode = Nav.Pan;
						}
						else if ( e.alt )
						{
							// Hammer (the same as Source Filmmaker and Maya): Alt+left orbits, Alt+right
							// dollies, Alt+middle pans. Alt+double-click is left for selecting coplanar faces.
							mode = e.button switch { 0 when e.clickCount < 2 => Nav.Orbit, 1 => Nav.Dolly, 2 => Nav.Pan, _ => Nav.None };
						}
						else
						{
							// A plain middle drag is the lasso (Alt+middle pans)
							mode = e.button switch { 1 => Nav.Look, _ => Nav.None };
						}

						if ( mode == Nav.None )
							return false;

						_nav = mode;
						_navMoved = false;
						_navAltRight = e.alt && e.button == 1;
						GUIUtility.hotControl = id;

						_anchorGui = e.mousePosition;
						_navRect = rect;

						// Every camera drag freezes and hides the cursor where you clicked, like Hammer,
						// so it never runs out of the view
						if ( CanWarp ) LockCursor( true );
						else EditorGUIUtility.SetWantsMouseJumping( 1 );

						if ( mode == Nav.Orbit )
						{
							_orbitPoint = OrbitPoint( rect );

							// What it turns around becomes the focus, so zooming afterwards heads there
							var from = CameraPosition;
							Distance = Mathf.Max( Vector3.Distance( from, _orbitPoint ), 0.05f );
							Pivot = from + Rotation * Vector3.forward * Distance;
						}

						if ( mode == Nav.Look )
						{
							_flying = true;
							_flyKeys.Clear();
							_flyRamp = 0;
							_lastFlyTime = EditorApplication.timeSinceStartup;
						}

						e.Use();
						return true;
					}

					case EventType.MouseDrag when GUIUtility.hotControl == id && _nav != Nav.None:
					{
						var delta = e.delta;

						// With the cursor held in place, movement is read from the OS cursor instead
						// (Unity's deltas include the jumps back)
						if ( _cursorLocked )
						{
							PollLockedLook();
							e.Use();
							window.Repaint();
							return true;
						}

						_navMoved = true;

						switch ( _nav )
						{
							case Nav.Pan: Pan( delta, rect ); break;
							case Nav.Look: Look( delta ); break;
							case Nav.LookOnly: Look( delta ); break;
							case Nav.Orbit: Orbit( delta ); break;
							case Nav.Dolly: MoveForward( -delta.y * FlySpeedUnits * SourceSpace.UnitScale * 0.004f ); break;
						}

						e.Use();
						window.Repaint();
						return true;
					}

					case EventType.MouseUp when GUIUtility.hotControl == id:
					{
						GUIUtility.hotControl = 0;
						EditorGUIUtility.SetWantsMouseJumping( 0 );
						LockCursor( false );
						_nav = Nav.None;
						_flying = false;
						_flyKeys.Clear();

						// Alt+right click without a drag isn't a zoom: the tool gets it (copy a
						// face's texture onto another)
						if ( _navAltRight && !_navMoved ) return false;

						e.Use();
						return true;
					}

					case EventType.KeyDown when _nav == Nav.Look:
						if ( e.keyCode != KeyCode.None ) _flyKeys.Add( e.keyCode );
						e.Use();
						return true;

					case EventType.KeyUp:
						_flyKeys.Remove( e.keyCode );
						return false;
				}

				return false;
			}

			[NonSerialized] public bool FlyMode;
			[NonSerialized] bool _skipMove;
			[NonSerialized] Vector2Int _anchorScreen;
			[NonSerialized] Vector2 _anchorGui;
			[NonSerialized] Rect _navRect;
			[NonSerialized] bool _cursorLocked;
			static Texture2D _blankCursor;

			/// <summary>
			/// For automated tests that send fake mouse events: don't move the real cursor.
			/// </summary>
			internal static bool DisableCursorWarp;

			/// <summary>
			/// While true the window hides the cursor over this view (see HammerWindow.DrawViewport).
			/// </summary>
			public bool HidesCursor => _cursorLocked || FlyMode;

			void LockCursor( bool on )
			{
				if ( on == _cursorLocked ) return;
				_cursorLocked = on;

				if ( on )
				{
					_anchorScreen = CursorPosition();
					if ( FlyMode ) _anchorGui = new Vector2( -1, -1 );
					if ( _blankCursor == null )
					{
						_blankCursor = new Texture2D( 4, 4, TextureFormat.RGBA32, false ) { hideFlags = HideFlags.HideAndDontSave };
						_blankCursor.SetPixels( new Color[16] );
						_blankCursor.Apply();
					}
					Cursor.SetCursor( _blankCursor, Vector2.zero, CursorMode.ForceSoftware );
				}
				else
				{
					Cursor.SetCursor( null, Vector2.zero, CursorMode.Auto );
				}
			}

			public void SetFlyMode( bool on )
			{
				FlyMode = on;
				if ( CanWarp ) LockCursor( on );
				_nav = on ? Nav.Look : Nav.None;
				_flying = on;
				_flyKeys.Clear();
				_flyRamp = 0;
				_skipMove = false;
				_lastFlyTime = EditorApplication.timeSinceStartup;
			}

			/// <summary>
			/// Fly mode (Z): mouse movement looks around, the cursor is kept in the middle of the view.
			/// </summary>
			bool FlyModeGUI( Event e, Rect rect, HammerWindow window )
			{
				switch ( e.type )
				{
					case EventType.MouseMove:
					case EventType.MouseDrag:
						if ( _cursorLocked ) PollLockedLook();
						else Look( e.delta );

						e.Use();
						window.Repaint();
						return true;

					case EventType.MouseDown:
					case EventType.KeyDown when e.keyCode == KeyCode.Escape:
						SetFlyMode( false );
						e.Use();
						window.Repaint();
						return true;

					case EventType.KeyDown:
						if ( e.keyCode != KeyCode.None && e.keyCode != KeyCode.Z ) _flyKeys.Add( e.keyCode );
						return false; // let Z reach its shortcut

					case EventType.KeyUp:
						_flyKeys.Remove( e.keyCode );
						return false;

					case EventType.ScrollWheel:
						FlySpeedUnits = Mathf.Clamp( FlySpeedUnits * (e.delta.y > 0 ? 1 / 1.25f : 1.25f), 16, HammerSettings.ForwardSpeedMax );
						_speedShownUntil = EditorApplication.timeSinceStartup + 1.5;
						e.Use();
						window.Repaint();
						return true;
				}

				return false;
			}

			/// <summary>
			/// Look by however far the real cursor moved from where it's held, then put it back.
			/// Also polled from the editor update so looking doesn't depend on Unity's mouse events.
			/// </summary>
			public bool PollLockedLook()
			{
				if ( !_cursorLocked ) return false;

				var p = CursorPosition();
				var d = p - _anchorScreen;
				if ( d == Vector2Int.zero ) return false;

				SetCursorPosition( _anchorScreen );
				var delta = new Vector2( d.x, d.y ) / EditorGUIUtility.pixelsPerPoint;
				_navMoved = true;
				switch ( _nav )
				{
					case Nav.Pan: Pan( delta, _navRect ); break;
					case Nav.Orbit: Orbit( delta ); break;
					case Nav.Dolly: MoveForward( -delta.y * FlySpeedUnits * SourceSpace.UnitScale * 0.004f ); break;
					default: Look( delta ); break;
				}
				return true;
			}

#if UNITY_EDITOR_WIN
			struct POINT { public int X, Y; }

			[System.Runtime.InteropServices.DllImport( "user32.dll" )]
			static extern bool SetCursorPos( int x, int y );

			[System.Runtime.InteropServices.DllImport( "user32.dll" )]
			static extern bool GetCursorPos( out POINT point );

			static Vector2Int CursorPosition() => GetCursorPos( out var p ) ? new Vector2Int( p.X, p.Y ) : Vector2Int.zero;

			static void SetCursorPosition( Vector2Int p ) => SetCursorPos( p.x, p.y );
#else
			static Vector2Int CursorPosition() => Vector2Int.zero;
			static void SetCursorPosition( Vector2Int p ) { }
#endif

#if UNITY_EDITOR_WIN
			static bool CanWarp => !DisableCursorWarp;
#else
			static bool CanWarp => false;
#endif

			enum Nav
			{
				None,
				Pan,
				Look,
				Orbit,
				Dolly,
				LookOnly,
			}

			static readonly int NavigationHash = "HammerViewNavigation".GetHashCode();

			[NonSerialized] Nav _nav;
			[NonSerialized] float _flyRamp;
			[NonSerialized] bool _navMoved;
			[NonSerialized] bool _navAltRight;
			static bool _shiftHeld;
			static bool _ctrlHeld;

			bool _looking => _nav == Nav.Look;

			public void ZoomOrthoAt( Vector2 mouse, Rect rect, float factor )
			{
				var before = OrthoWorld( mouse, rect );
				OrthoSize = Mathf.Clamp( OrthoSize * factor, 0.01f, 5000 );
				var after = OrthoWorld( mouse, rect );
				Pivot += before - after;
			}

			Vector3 OrthoWorld( Vector2 mouse, Rect rect )
			{
				var rot = Rotation;
				var unitsPerPixel = 2 * OrthoSize / rect.height;
				var offset = new Vector2( mouse.x - rect.center.x, rect.center.y - mouse.y ) * unitsPerPixel;
				return Pivot + rot * Vector3.right * offset.x + rot * Vector3.up * offset.y;
			}

			void Pan( Vector2 delta, Rect rect )
			{
				var rot = Rotation;
				float unitsPerPixel = IsOrtho
					? 2 * OrthoSize / rect.height
					: 2 * Distance * Mathf.Tan( 30 * Mathf.Deg2Rad ) / rect.height;

				Pivot += (rot * Vector3.left * delta.x + rot * Vector3.up * delta.y) * unitsPerPixel;
			}

			const float LookSensitivity = 0.2f; // degrees per pixel

			void Look( Vector2 delta )
			{
				// Rotate about the camera, not the pivot
				var position = CameraPosition;
				Yaw += delta.x * LookSensitivity;
				Pitch = Mathf.Clamp( Pitch + delta.y * LookSensitivity, -89.9f, 89.9f );
				Pivot = position + Rotation * Vector3.forward * Distance;
			}

			[NonSerialized] Vector3 _orbitPoint;

			/// <summary>
			/// Alt+drag orbits whatever is under the cursor: a Hammer mesh, then anything with a
			/// collider, otherwise a point under the cursor at the current focus distance.
			/// </summary>
			/// <summary>
			/// What Alt+drag turns around (Hammer: "rotate around the center of the screen"): what's
			/// in the middle of the view, else where the middle of the view meets the ground, else
			/// the focus point (Shift+A puts that on the selection). Turning round a point just in
			/// front of the camera would only look like looking around.
			/// </summary>
			Vector3 OrbitPoint( Rect rect )
			{
				if ( OrbitPointUnder( rect.center, rect, out var middle ) ) return middle;

				var camera = EnsureCamera();
				var ray = camera.ViewportPointToRay( new Vector3( 0.5f, 0.5f, 0 ) );
				var ground = new Plane( Workplane.Up, Workplane.Origin );
				if ( ground.Raycast( ray, out var enter ) && enter < camera.farClipPlane * 0.5f )
					return ray.GetPoint( enter );

				return Pivot;
			}

			bool OrbitPointUnder( Vector2 mouse, Rect rect, out Vector3 point )
			{
				var camera = EnsureCamera();
				var ray = camera.ViewportPointToRay( new Vector3( (mouse.x - rect.x) / rect.width, 1 - (mouse.y - rect.y) / rect.height, 0 ) );

				var meshes = UnityEngine.Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None )
					.Where( m => m.isActiveAndEnabled && !SceneVisibilityManager.instance.IsHidden( m.gameObject ) );

				var best = float.MaxValue;
				if ( MeshPicking.RaycastFace( ray, meshes, out var hit ) )
					best = hit.Distance;

				// Edit mode doesn't move colliders along with their objects until asked
				Physics.SyncTransforms();
				if ( Physics.Raycast( ray, out var physicsHit, camera.farClipPlane ) && physicsHit.distance < best )
					best = physicsHit.distance;

				point = best < float.MaxValue ? ray.GetPoint( best ) : default;
				return best < float.MaxValue;
			}

			void Orbit( Vector2 delta )
			{
				var before = Rotation;
				var camera = CameraPosition;

				Yaw += delta.x * 0.3f;
				Pitch = Mathf.Clamp( Pitch + delta.y * 0.3f, -89.9f, 89.9f );

				// Swing the camera around the orbit point, then put the focus back in front of it
				var turn = Rotation * Quaternion.Inverse( before );
				camera = _orbitPoint + turn * (camera - _orbitPoint);
				Pivot = camera + Rotation * Vector3.forward * Distance;
			}

			void MoveForward( float amount )
			{
				// Move the camera and its focus point together, so the wheel always moves at the
				// same speed however far away the focus point is
				Pivot += Rotation * Vector3.forward * amount;
			}

			/// <summary>
			/// WASD / Q / E flying while the right mouse is held, with a short acceleration ramp like
			/// Hammer's camera. Returns true if the camera moved.
			/// </summary>
			public bool UpdateFly( HammerWindow window )
			{
				var now = EditorApplication.timeSinceStartup;
				var dt = (float)Math.Min( now - _lastFlyTime, 0.1 );
				_lastFlyTime = now;

				if ( !_looking || _flyKeys.Count == 0 )
				{
					_flyRamp = 0;
					return false;
				}

				var move = Vector3.zero;
				if ( _flyKeys.Contains( KeyCode.W ) ) move += Vector3.forward;
				if ( _flyKeys.Contains( KeyCode.S ) ) move += Vector3.back;
				if ( _flyKeys.Contains( KeyCode.A ) ) move += Vector3.left;
				if ( _flyKeys.Contains( KeyCode.D ) ) move += Vector3.right;
				// Up and down: Z / X (Source Filmmaker), or E / Q
				if ( _flyKeys.Contains( KeyCode.E ) || _flyKeys.Contains( KeyCode.Z ) ) move += Vector3.up;
				if ( _flyKeys.Contains( KeyCode.Q ) || _flyKeys.Contains( KeyCode.X ) ) move += Vector3.down;

				if ( move == Vector3.zero )
				{
					_flyRamp = 0;
					return false;
				}

				// Ease up to full speed over about a third of a second
				_flyRamp = Mathf.Min( 1, _flyRamp + dt / 0.3f );
				var ease = _flyRamp * _flyRamp * (3 - 2 * _flyRamp);

				// Shift speeds up, Ctrl slows down
				var boost = _shiftHeld || _flyKeys.Contains( KeyCode.LeftShift ) || _flyKeys.Contains( KeyCode.RightShift ) ? 3f : 1f;
				if ( _ctrlHeld || _flyKeys.Contains( KeyCode.LeftControl ) || _flyKeys.Contains( KeyCode.RightControl ) ) boost *= 0.25f;
				var speed = Mathf.Max( FlySpeedUnits, 16 ) * SourceSpace.UnitScale * boost * Mathf.Max( ease, 0.15f );

				Pivot += Rotation * move.normalized * speed * dt;
				return true;
			}

			public void DrawGrid( Rect rect )
			{
				var grid = HammerSettings.GridSize * SourceSpace.UnitScale;
				var old = Handles.zTest;

				if ( IsOrtho )
				{
					var rot = Rotation;
					var right = rot * Vector3.right;
					var up = rot * Vector3.up;
					var unitsPerPixel = 2 * OrthoSize / rect.height;

					// Hammer hides grid lines that would be closer than a few pixels apart
					var spacing = grid;
					while ( spacing / unitsPerPixel < 10 ) spacing *= 2;

					var halfW = OrthoSize * rect.width / rect.height;
					var halfH = OrthoSize;
					var cx = Vector3.Dot( Pivot, right );
					var cy = Vector3.Dot( Pivot, up );
					var depth = Pivot - right * cx - up * cy;

					Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
					DrawLines( right, up, depth, cx, halfW, cy, halfH, spacing );
					DrawLines( up, right, depth, cy, halfH, cx, halfW, spacing );
				}

				Handles.zTest = old;
			}

			/// <summary>
			/// The 3D view's ground grid. Drawn while the camera renders, so it's hidden by geometry.
			/// </summary>
			static Material _gridMaterial;

			/// <summary>
			/// Vertex-coloured lines, blended, depth tested against the scene.
			/// </summary>
			static Material GridMaterial
			{
				get
				{
					if ( _gridMaterial != null ) return _gridMaterial;
					_gridMaterial = new Material( Shader.Find( "Hidden/Internal-Colored" ) ) { hideFlags = HideFlags.HideAndDontSave };
					_gridMaterial.SetInt( "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha );
					_gridMaterial.SetInt( "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha );
					_gridMaterial.SetInt( "_Cull", (int)UnityEngine.Rendering.CullMode.Off );
					_gridMaterial.SetInt( "_ZWrite", 0 );
					_gridMaterial.SetInt( "_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual );
					return _gridMaterial;
				}
			}

			public void DrawGround()
			{
				if ( IsOrtho ) return;
				var grid = HammerSettings.GridSize * SourceSpace.UnitScale;

				// Worked out on the workplane (the world's ground when there isn't one)
				var camera = Workplane.ToLocal( CameraPosition );
				var height = Mathf.Abs( camera.y );

				// One grid round the camera that fades out smoothly by its edge, rather than lines
				// piling up into a bright band at the horizon
				var spacing = grid;
				var reach = Mathf.Max( height * 10, Distance * 4, grid * 48 );
				while ( reach / spacing > 128 ) spacing *= 2;

				var cx = Mathf.Round( camera.x / spacing ) * spacing;
				var cz = Mathf.Round( camera.z / spacing ) * spacing;
				var count = Mathf.CeilToInt( reach / spacing );
				var fadeStart = reach * 0.25f;
				var center = new Vector2( camera.x, camera.z );

				var old = Handles.zTest;
				GridMaterial.SetPass( 0 );
				GL.Begin( GL.LINES );

				// Each line in short pieces, each piece's ends faded by their distance from the camera
				const int pieces = 16;
				void Line( Vector3 a, Vector3 b, Color color )
				{
					for ( int k = 0; k < pieces; k++ )
					{
						var p0 = Vector3.Lerp( a, b, k / (float)pieces );
						var p1 = Vector3.Lerp( a, b, (k + 1) / (float)pieces );
						var f0 = Fade( p0 );
						var f1 = Fade( p1 );
						if ( f0 <= 0 && f1 <= 0 ) continue;
						GL.Color( new Color( color.r, color.g, color.b, color.a * f0 ) );
						GL.Vertex( Workplane.ToWorld( p0 ) );
						GL.Color( new Color( color.r, color.g, color.b, color.a * f1 ) );
						GL.Vertex( Workplane.ToWorld( p1 ) );
					}
				}

				float Fade( Vector3 p )
				{
					var d = Vector2.Distance( new Vector2( p.x, p.z ), center );
					return 1 - Mathf.SmoothStep( 0, 1, Mathf.InverseLerp( fadeStart, reach, d ) );
				}

				for ( int i = -count; i <= count; i++ )
				{
					var x = cx + i * spacing;
					var z = cz + i * spacing;
					// Orange every 8th line, teal axes. Counted in the lines actually drawn: zoomed
					// out the drawn step is a multiple of the grid, and counting in grid steps would
					// make every line orange
					var step = Mathf.Max( spacing, grid * 8 ) == spacing ? spacing * 8 : grid * 8;
					Color Colour( float v ) => Mathf.Abs( v ) < spacing * 0.5f ? HammerIcons.GridAxis
						: Mathf.Abs( Mathf.Repeat( v + step * 0.5f, step ) - step * 0.5f ) < spacing * 0.5f ? HammerIcons.GridMajor : HammerIcons.GridMinor;

					Line( new Vector3( x, 0, cz - reach ), new Vector3( x, 0, cz + reach ), Colour( x ) );
					Line( new Vector3( cx - reach, 0, z ), new Vector3( cx + reach, 0, z ), Colour( z ) );
				}

				GL.End();
				Handles.zTest = old;
			}

			static void DrawLines( Vector3 axis, Vector3 across, Vector3 depth, float center, float half, float acrossCenter, float acrossHalf, float spacing )
			{
				var minor = new List<Vector3>();
				var major = new List<Vector3>();
				var origin = new List<Vector3>();

				var start = Mathf.Floor( (center - half) / spacing );
				var end = Mathf.Ceil( (center + half) / spacing );

				for ( var i = start; i <= end; i++ )
				{
					var t = i * spacing;
					var a = depth + axis * t + across * (acrossCenter - acrossHalf);
					var b = depth + axis * t + across * (acrossCenter + acrossHalf);

					// Hammer highlights every 8th line, and the axes
					var n = Mathf.RoundToInt( i );
					var list = n == 0 ? origin : n % 8 == 0 ? major : minor;
					list.Add( a );
					list.Add( b );
				}

				// Fainter than in 3D: on the black 2D background full-strength lines drown the wireframe
				var faint = HammerIcons.GridMinor;
				faint.a *= 0.55f;
				Handles.color = faint;
				Handles.DrawLines( minor.ToArray() );
				Handles.color = HammerIcons.GridMajor;
				Handles.DrawLines( major.ToArray() );
				Handles.color = HammerIcons.GridAxis;
				Handles.DrawLines( origin.ToArray() );
			}

			static GUIStyle _headerStyle;

			static GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle( EditorStyles.miniLabel )
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = 11,
				normal = { textColor = new Color( 0.85f, 0.85f, 0.85f ) },
			};

			/// <summary>
			/// Hammer's view header: a dropdown with the view's name and a layout button, top right,
			/// and a red frame around the active view.
			/// </summary>
			public void DrawLabel( Rect rect, bool active )
			{
				Handles.BeginGUI();

				var label = LabelRect( rect );
				var hover = label.Contains( Event.current.mousePosition );
				EditorGUI.DrawRect( label, new Color( 0.13f, 0.13f, 0.13f, 0.95f ) );
				EditorGUI.DrawRect( new Rect( label.x, label.yMax - 1, label.width, 1 ), hover ? HammerIcons.Accent : new Color( 0.3f, 0.3f, 0.3f ) );
				GUI.Label( label, DisplayName, HeaderStyle );

				var layout = LayoutRect( rect );
				EditorGUI.DrawRect( layout, new Color( 0.13f, 0.13f, 0.13f, 0.95f ) );
				var icon = new Rect( layout.x + 5, layout.y + 3, 12, 12 );
				HammerIcons.DrawLayoutIcon( icon, new Rect( 0, 0, 0.5f, 0.5f ), new Rect( 0.5f, 0, 0.5f, 0.5f ), new Rect( 0, 0.5f, 0.5f, 0.5f ), new Rect( 0.5f, 0.5f, 0.5f, 0.5f ) );

				if ( EditorApplication.timeSinceStartup < _speedShownUntil )
				{
					var text = $"Speed {FlySpeedUnits:0}";
					var size = EditorStyles.whiteMiniLabel.CalcSize( new GUIContent( text ) );
					EditorGUI.DrawRect( new Rect( rect.x + 4, rect.y + 5, size.x + 8, 18 ), new Color( 0, 0, 0, 0.6f ) );
					GUI.Label( new Rect( rect.x + 8, rect.y + 6, size.x, 16 ), text, EditorStyles.whiteMiniLabel );
				}

				// The real cursor is hidden while looking or orbiting: a small cross stands in for it
				if ( HidesCursor )
				{
					// Always in the middle of the view, like Hammer; whole pixels so both arms match
					var p = new Vector2( Mathf.Floor( rect.center.x ), Mathf.Floor( rect.center.y ) );
					var dark = new Color( 0, 0, 0, 0.8f );
					EditorGUI.DrawRect( new Rect( p.x - 7, p.y - 1, 15, 3 ), dark );
					EditorGUI.DrawRect( new Rect( p.x - 1, p.y - 7, 3, 15 ), dark );
					EditorGUI.DrawRect( new Rect( p.x - 6, p.y, 13, 1 ), Color.white );
					EditorGUI.DrawRect( new Rect( p.x, p.y - 6, 1, 13 ), Color.white );
				}

				if ( active )
				{
					var c = HammerIcons.ActiveView;
					EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, 1 ), c );
					EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), c );
					EditorGUI.DrawRect( new Rect( rect.x, rect.y, 1, rect.height ), c );
					EditorGUI.DrawRect( new Rect( rect.xMax - 1, rect.y, 1, rect.height ), c );
				}

				Handles.EndGUI();
			}
		}
	}
}
