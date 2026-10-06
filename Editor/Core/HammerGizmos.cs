using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Move / rotate / scale gizmos and sliders that use <see cref="HammerGUI"/>'s conversions,
	/// so they work in the Hammer window's viewports as well as Unity's scene view. Same calling
	/// pattern as Unity's Handles: pass the current value, get the new one back, and GUI.changed
	/// is set while dragging.
	/// </summary>
	public static class HammerGizmos
	{
		const float PickDistance = 7;

		/// <summary>
		/// Hammer's gizmos are a little bigger than one handle size, with thicker lines.
		/// </summary>
		public const float GizmoScale = 1.2f;

		/// <summary>
		/// Extra pixels around every handle part that still count as grabbing it: more in the 2D
		/// views, where an axis is a thin flat line with nothing else to aim at.
		/// </summary>
		static float GrabMargin => HammerGUI.Camera != null && HammerGUI.Camera.orthographic ? 12 : 6;

		static readonly int PositionHash = "HammerPosition".GetHashCode();
		static readonly int RotationHash = "HammerRotation".GetHashCode();
		static readonly int ScaleHash = "HammerScale".GetHashCode();
		static readonly int SliderHash = "HammerSlider".GetHashCode();

		// Hammer's colours, by Hammer axis: X red, Y green, Z (up) blue. Hammer X/Y/Z are Unity
		// Z/-X/Y, so Unity's right is green, up is blue and forward is red
		static readonly Color RightColor = new( 0.25f, 0.85f, 0.12f );
		static readonly Color UpColor = new( 0.15f, 0.45f, 1.0f );
		static readonly Color ForwardColor = new( 0.92f, 0.12f, 0.1f );
		static readonly Color HotColor = new( 1.0f, 0.85f, 0.2f );
		static readonly Color CentreColor = new( 0.85f, 0.25f, 0.9f );

		// Drag state, shared because only one control can be hot at a time
		static Vector3 _startPosition;
		static Quaternion _startRotation;
		static Vector3 _startScale;
		static Vector3 _startHit;
		static float _startParam;
		static Vector2 _startMouse;

		static int _centreId;

		/// <summary>
		/// Is the move gizmo's purple centre being dragged? (Hammer sticks the selection to the
		/// surface under the mouse while it is.)
		/// </summary>
		public static bool CentreDragging => _centreId != 0 && GUIUtility.hotControl == _centreId;

		static Color AxisColor( int axis ) => axis == 0 ? RightColor : axis == 1 ? UpColor : ForwardColor;
		static Vector3 Axis( int axis ) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

		static Color ControlColor( int id, Color normal )
		{
			if ( GUIUtility.hotControl == id || (GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id) )
				return HotColor;
			return normal;
		}

		/// <summary>
		/// Closest-approach parameter along a line (origin + dir * t) to a ray.
		/// </summary>
		static float LineParam( Ray ray, Vector3 origin, Vector3 dir )
		{
			var w = origin - ray.origin;
			var b = Vector3.Dot( dir, ray.direction );
			var d = Vector3.Dot( dir, w );
			var e = Vector3.Dot( ray.direction, w );
			var denom = 1 - b * b;
			if ( denom < 1e-6f ) return 0;
			return (b * e - d) / denom;
		}

		static bool RayPlane( Ray ray, Vector3 normal, Vector3 point, out Vector3 hit )
		{
			var plane = new Plane( normal, point );
			if ( plane.Raycast( ray, out var t ) || new Plane( -normal, point ).Raycast( ray, out t ) )
			{
				hit = ray.GetPoint( t );
				return true;
			}

			hit = point;
			return false;
		}

		// Control IDs for the Hammer window's views. IMGUI numbers controls in the order they're
		// made across the whole window, so anything drawn earlier that changes with the selection
		// (the panel, the status bar, another view) would shift a gizmo's number in the middle of
		// a drag (an extrude adds faces) and the drag would be dropped. Here each gizmo part keeps
		// one ID per view, picked by its place among that view's gizmos.
		static readonly System.Collections.Generic.Dictionary<(int View, int Hash, int Index), int> _ids = new();
		static HammerView _idView;
		static int _idIndex;

		// Far above the numbers IMGUI hands out itself
		const int IdBase = 0x48A00000;

		static int ControlID( int hash )
		{
			var view = HammerViews.Current;
			if ( view == null || view.Window is SceneView || view.Camera == null )
				return GUIUtility.GetControlID( hash, FocusType.Passive );

			// A new HammerView is made for every view on every event: start counting again
			if ( !ReferenceEquals( view, _idView ) ) { _idView = view; _idIndex = 0; }

			var key = (view.Camera.GetInstanceID(), hash, _idIndex++);
			if ( !_ids.TryGetValue( key, out var id ) ) _ids[key] = id = IdBase + _ids.Count;
			return id;
		}

		static bool BeginDrag( int id, Event e )
		{
			if ( e.GetTypeForControl( id ) != EventType.MouseDown || e.button != 0 || e.alt )
				return false;

			if ( HandleUtility.nearestControl != id || GUIUtility.hotControl != 0 )
				return false;

			GUIUtility.hotControl = id;
			_startMouse = e.mousePosition;
			e.Use();
			return true;
		}

		static bool Dragging( int id, Event e ) => GUIUtility.hotControl == id && e.GetTypeForControl( id ) == EventType.MouseDrag;

		static void EndDrag( int id, Event e )
		{
			if ( GUIUtility.hotControl == id && e.GetTypeForControl( id ) == EventType.MouseUp )
			{
				GUIUtility.hotControl = 0;
				e.Use();
			}
		}

		/// <summary>
		/// Hammer's centre marker: a diamond facing the camera, with a dark outline.
		/// </summary>
		public static void Diamond( Vector3 position, float size, Color color )
		{
			var camera = HammerGUI.Camera;
			if ( camera == null ) return;
			var right = camera.transform.right * size;
			var up = camera.transform.up * size;
			var points = new[] { position + up, position + right, position - up, position - right };
			Handles.color = color;
			Handles.DrawAAConvexPolygon( points );
			Handles.color = new Color( 0, 0, 0, 0.6f * color.a );
			Handles.DrawAAPolyLine( 1.5f, points[0], points[1], points[2], points[3], points[0] );
		}

		public static Vector3 PositionHandle( Vector3 position, Quaternion rotation, bool pivotDiamond = false )
		{
			var e = Event.current;
			var size = HammerGUI.HandleSize( position ) * GizmoScale;
			var result = position;
			var camera = HammerGUI.Camera;
			var viewDir = camera != null ? camera.transform.forward : Vector3.forward;

			// Axis arrows
			for ( int i = 0; i < 3; i++ )
			{
				var id = ControlID( PositionHash );
				var dir = rotation * Axis( i );

				// Hide an axis pointing straight at the camera (the 2D views' depth axis)
				var facing = Mathf.Abs( Vector3.Dot( dir, viewDir ) ) > 0.98f;
				var end = position + dir * size;

				if ( e.type == EventType.Layout && !facing )
					HandleUtility.AddControl( id, Mathf.Max( 0, HammerGUI.DistanceToSegment( e.mousePosition, position, end + (end - position) * 0.15f ) - GrabMargin ) );

				if ( !facing && BeginDrag( id, e ) )
				{
					_startPosition = position;
					_startParam = LineParam( HammerGUI.GUIToRay( e.mousePosition ), position, dir );
				}

				if ( Dragging( id, e ) )
				{
					var t = LineParam( HammerGUI.GUIToRay( e.mousePosition ), _startPosition, dir );
					result = _startPosition + dir * (t - _startParam);
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint && !facing )
				{
					// Hammer style: a thin line with a cone tip
					Handles.color = ControlColor( id, AxisColor( i ) );
					Handles.DrawAAPolyLine( 3.5f, position, end );
					Handles.ConeHandleCap( id, end, Quaternion.LookRotation( dir ), size * 0.16f, EventType.Repaint );
				}
			}

			// Plane squares (two axes at once)
			for ( int i = 0; i < 3; i++ )
			{
				var id = ControlID( PositionHash );
				var a = rotation * Axis( (i + 1) % 3 );
				var b = rotation * Axis( (i + 2) % 3 );
				var normal = rotation * Axis( i );
				// A small solid square out between the two axes it moves along
				var o = size * 0.3f;
				var s = size * 0.44f;
				var quad = new[] { position + a * o + b * o, position + a * s + b * o, position + a * s + b * s, position + a * o + b * s };

				// Only the plane facing the camera is useful in the 2D views
				var edgeOn = Mathf.Abs( Vector3.Dot( normal, viewDir ) ) < 0.2f;

				if ( e.type == EventType.Layout && !edgeOn )
					HandleUtility.AddControl( id, InsideQuad( e.mousePosition, quad ) ? 0 : Mathf.Max( 0, DistanceToQuad( e.mousePosition, quad ) - GrabMargin ) );

				if ( !edgeOn && BeginDrag( id, e ) )
				{
					_startPosition = position;
					RayPlane( HammerGUI.GUIToRay( e.mousePosition ), normal, position, out _startHit );
				}

				if ( Dragging( id, e ) )
				{
					RayPlane( HammerGUI.GUIToRay( e.mousePosition ), normal, _startPosition, out var hit );
					var delta = hit - _startHit;
					result = _startPosition + a * Vector3.Dot( delta, a ) + b * Vector3.Dot( delta, b );
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint && !edgeOn )
				{
					Handles.color = ControlColor( id, AxisColor( i ) );
					Handles.DrawAAConvexPolygon( quad );
				}
			}

			// Centre: free move in the view plane (and onto surfaces, see CentreDragging)
			{
				var id = ControlID( PositionHash );
				_centreId = id;
				var g = HammerGUI.WorldToGUI( position );

				if ( e.type == EventType.Layout )
					HandleUtility.AddControl( id, Mathf.Max( 0, Vector2.Distance( e.mousePosition, g ) - 6 - GrabMargin ) );

				if ( BeginDrag( id, e ) )
				{
					_startPosition = position;
					RayPlane( HammerGUI.GUIToRay( e.mousePosition ), viewDir, position, out _startHit );
				}

				if ( Dragging( id, e ) )
				{
					RayPlane( HammerGUI.GUIToRay( e.mousePosition ), viewDir, _startPosition, out var hit );
					result = _startPosition + (hit - _startHit);
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint )
				{
					// (a diamond marks a pivot being placed; a plain move gizmo has a small dot)
					if ( pivotDiamond )
						Diamond( position, size * 0.07f, ControlColor( id, CentreColor ) );
					else
					{
						Handles.color = ControlColor( id, CentreColor );
						Handles.DotHandleCap( id, position, Quaternion.identity, size * 0.035f, EventType.Repaint );
					}
				}
			}

			// (dragging an axis that points almost straight at the camera can fly off to infinity)
			if ( !float.IsFinite( result.x ) || !float.IsFinite( result.y ) || !float.IsFinite( result.z ) || (result - position).magnitude > 1e5f )
				return position;
			return result;
		}

		public static Quaternion RotationHandle( Quaternion rotation, Vector3 position )
		{
			var e = Event.current;
			var size = HammerGUI.HandleSize( position ) * GizmoScale;
			var result = rotation;
			var camera = HammerGUI.Camera;
			var viewDir = camera != null ? camera.transform.forward : Vector3.forward;

			for ( int i = 0; i < 3; i++ )
			{
				var id = ControlID( RotationHash );
				var axis = rotation * Axis( i );

				// Rings seen edge-on can't be grabbed sensibly
				var edgeOn = Mathf.Abs( Vector3.Dot( axis, viewDir ) ) < 0.05f;

				if ( e.type == EventType.Layout && !edgeOn )
					HandleUtility.AddControl( id, Mathf.Max( 0, DistanceToCircle( e.mousePosition, position, axis, size ) - GrabMargin ) );

				if ( !edgeOn && BeginDrag( id, e ) )
				{
					_startRotation = rotation;
					RayPlane( HammerGUI.GUIToRay( e.mousePosition ), axis, position, out _startHit );
					_startPosition = position;
				}

				if ( Dragging( id, e ) )
				{
					var startAxis = _startRotation * Axis( i );
					RayPlane( HammerGUI.GUIToRay( e.mousePosition ), startAxis, _startPosition, out var hit );
					var from = Vector3.ProjectOnPlane( _startHit - _startPosition, startAxis );
					var to = Vector3.ProjectOnPlane( hit - _startPosition, startAxis );
					var angle = Vector3.SignedAngle( from, to, startAxis );
					result = Quaternion.AngleAxis( angle, startAxis ) * _startRotation;
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint )
					FrontArc( position, axis, size, ControlColor( id, AxisColor( i ) ) );
			}

			// Hammer's yellow ball in the middle: drag it to turn freely about any axis (shown
			// when the mouse is near it)
			{
				var id = ControlID( RotationHash );
				var g = HammerGUI.WorldToGUI( position );
				var ballPixels = Vector2.Distance( g, HammerGUI.WorldToGUI( position + (camera != null ? camera.transform.right : Vector3.right) * size * 0.8f ) );

				if ( e.type == EventType.Layout )
					HandleUtility.AddControl( id, Mathf.Max( 0, Vector2.Distance( e.mousePosition, g ) - ballPixels ) + 2 );

				if ( BeginDrag( id, e ) )
					_startRotation = rotation;

				if ( Dragging( id, e ) && camera != null )
				{
					var d = e.mousePosition - _startMouse;
					if ( d.sqrMagnitude > 0.01f )
					{
						// Like rolling a trackball: the turn is about the axis across the drag
						var axis = (camera.transform.up * d.x + camera.transform.right * d.y).normalized;
						result = Quaternion.AngleAxis( d.magnitude * 0.5f, axis ) * _startRotation;
					}
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint && (GUIUtility.hotControl == id || (GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id)) )
				{
					Handles.color = new Color( 1.0f, 0.9f, 0.2f, 0.25f );
					var toCam = camera != null ? (camera.orthographic ? -camera.transform.forward : (camera.transform.position - position).normalized) : Vector3.back;
					Handles.DrawSolidDisc( position, toCam, size * 0.8f );
				}
			}

			if ( e.type == EventType.Repaint )
			{
				// Hammer's grey outline round the ball, and a yellow star in the middle
				var toCamera = camera != null ? (camera.orthographic ? -viewDir : (camera.transform.position - position).normalized) : Vector3.back;
				Handles.color = new Color( 0.45f, 0.5f, 0.6f, 0.9f );
				Handles.DrawWireDisc( position, toCamera, size * 1.02f, 2.5f );

				Handles.color = new Color( 1.0f, 0.9f, 0.2f );
				var up = camera != null ? camera.transform.up : Vector3.up;
				var r = size * 0.05f;
				for ( int k = 0; k < 3; k++ )
				{
					var d = Quaternion.AngleAxis( k * 60, toCamera ) * up;
					Handles.DrawAAPolyLine( 1.5f, position - d * r, position + d * r );
				}
			}

			return result;
		}

		/// <summary>
		/// The half of a rotation ring facing the camera (the back half is hidden, like Hammer).
		/// </summary>
		static void FrontArc( Vector3 center, Vector3 axis, float radius, Color color )
		{
			var camera = HammerGUI.Camera;
			var u = Vector3.Cross( axis, Mathf.Abs( axis.y ) < 0.9f ? Vector3.up : Vector3.right ).normalized;
			var v = Vector3.Cross( axis, u );
			const int segments = 64;

			Handles.color = color;
			var run = new System.Collections.Generic.List<Vector3>();
			for ( int k = 0; k <= segments; k++ )
			{
				var a = k * Mathf.PI * 2 / segments;
				var p = center + (u * Mathf.Cos( a ) + v * Mathf.Sin( a )) * radius;
				var toCamera = camera == null ? Vector3.back : camera.orthographic ? -camera.transform.forward : camera.transform.position - p;
				var front = Vector3.Dot( p - center, toCamera.normalized ) >= -radius * 0.02f;

				if ( front ) run.Add( p );
				else if ( run.Count > 0 ) { if ( run.Count > 1 ) Handles.DrawAAPolyLine( 4.5f, run.ToArray() ); run.Clear(); }
			}
			if ( run.Count > 1 ) Handles.DrawAAPolyLine( 4.5f, run.ToArray() );
		}

		public static Vector3 ScaleHandle( Vector3 scale, Vector3 position, Quaternion rotation, float size )
		{
			var e = Event.current;
			size *= GizmoScale;
			var result = scale;

			for ( int i = 0; i < 3; i++ )
			{
				var id = ControlID( ScaleHash );
				var dir = rotation * Axis( i );
				var end = position + dir * size;

				if ( e.type == EventType.Layout )
					HandleUtility.AddControl( id, Mathf.Max( 0, HammerGUI.DistanceToSegment( e.mousePosition, position, end + (end - position) * 0.15f ) - GrabMargin ) );

				if ( BeginDrag( id, e ) )
				{
					_startScale = scale;
					_startPosition = position;
					_startParam = Mathf.Max( LineParam( HammerGUI.GUIToRay( e.mousePosition ), position, dir ), size * 0.1f );
				}

				if ( Dragging( id, e ) )
				{
					var t = LineParam( HammerGUI.GUIToRay( e.mousePosition ), _startPosition, dir );
					var factor = t / _startParam;
					result = _startScale;
					result[i] = _startScale[i] * factor;
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint )
				{
					Handles.color = ControlColor( id, AxisColor( i ) );
					var tip = position + dir * size * (GUIUtility.hotControl == id ? result[i] / Mathf.Max( Mathf.Abs( _startScale[i] ), 1e-4f ) : 1);
					Handles.DrawAAPolyLine( 3.5f, position, tip );
					Handles.CubeHandleCap( id, tip, rotation, size * 0.12f, EventType.Repaint );
				}
			}

			// Plane squares: scale along two axes at once
			{
				var camera = HammerGUI.Camera;
				var viewDir = camera != null ? camera.transform.forward : Vector3.forward;

				for ( int i = 0; i < 3; i++ )
				{
					var id = ControlID( ScaleHash );
					int ia = (i + 1) % 3, ib = (i + 2) % 3;
					var a = rotation * Axis( ia );
					var b = rotation * Axis( ib );
					var normal = rotation * Axis( i );
					var o = size * 0.3f;
					var s = size * 0.44f;
					var quad = new[] { position + a * o + b * o, position + a * s + b * o, position + a * s + b * s, position + a * o + b * s };
					var edgeOn = Mathf.Abs( Vector3.Dot( normal, viewDir ) ) < 0.2f;

					if ( e.type == EventType.Layout && !edgeOn )
						HandleUtility.AddControl( id, InsideQuad( e.mousePosition, quad ) ? 0 : Mathf.Max( 0, DistanceToQuad( e.mousePosition, quad ) - GrabMargin ) );

					if ( !edgeOn && BeginDrag( id, e ) )
					{
						_startScale = scale;
						_startPosition = position;
						RayPlane( HammerGUI.GUIToRay( e.mousePosition ), normal, position, out _startHit );
					}

					if ( Dragging( id, e ) )
					{
						RayPlane( HammerGUI.GUIToRay( e.mousePosition ), normal, _startPosition, out var hit );
						var diagonal = (a + b).normalized;
						var start = Mathf.Max( Vector3.Dot( _startHit - _startPosition, diagonal ), size * 0.1f );
						var factor = Vector3.Dot( hit - _startPosition, diagonal ) / start;
						result = _startScale;
						result[ia] = _startScale[ia] * factor;
						result[ib] = _startScale[ib] * factor;
						GUI.changed = true;
						e.Use();
					}

					EndDrag( id, e );

					if ( e.type == EventType.Repaint && !edgeOn )
					{
						Handles.color = ControlColor( id, AxisColor( i ) );
						Handles.DrawAAConvexPolygon( quad );
					}
				}
			}

			// Centre cube: uniform scale by horizontal mouse movement
			{
				var id = ControlID( ScaleHash );
				var g = HammerGUI.WorldToGUI( position );

				if ( e.type == EventType.Layout )
					HandleUtility.AddControl( id, Mathf.Max( 0, Vector2.Distance( e.mousePosition, g ) - 7 - GrabMargin ) );

				if ( BeginDrag( id, e ) )
					_startScale = scale;

				if ( Dragging( id, e ) )
				{
					var factor = Mathf.Max( 0.01f, 1 + (e.mousePosition.x - _startMouse.x) / 100.0f );
					result = _startScale * factor;
					GUI.changed = true;
					e.Use();
				}

				EndDrag( id, e );

				if ( e.type == EventType.Repaint )
				{
					Handles.color = ControlColor( id, CentreColor );
					Handles.CubeHandleCap( id, position, rotation, size * 0.12f, EventType.Repaint );
				}
			}

			return result;
		}

		/// <summary>
		/// Drag a point along a direction.
		/// </summary>
		public static Vector3 Slider( Vector3 position, Vector3 direction, float size, Handles.CapFunction cap )
		{
			var e = Event.current;
			var id = ControlID( SliderHash );
			var result = position;
			var g = HammerGUI.WorldToGUI( position );

			if ( e.type == EventType.Layout )
				HandleUtility.AddControl( id, Mathf.Max( 0, Vector2.Distance( e.mousePosition, g ) - 8 - GrabMargin ) );

			if ( BeginDrag( id, e ) )
			{
				_startPosition = position;
				_startParam = LineParam( HammerGUI.GUIToRay( e.mousePosition ), position, direction );
			}

			if ( Dragging( id, e ) )
			{
				var t = LineParam( HammerGUI.GUIToRay( e.mousePosition ), _startPosition, direction );
				result = _startPosition + direction * (t - _startParam);
				GUI.changed = true;
				e.Use();
			}

			EndDrag( id, e );

			if ( e.type == EventType.Repaint )
			{
				var old = Handles.color;
				if ( GUIUtility.hotControl == id || HandleUtility.nearestControl == id ) Handles.color = HotColor;
				cap( id, position, Quaternion.identity, size, EventType.Repaint );
				Handles.color = old;
			}

			return result;
		}

		/// <summary>
		/// Drag a point within a plane.
		/// </summary>
		public static Vector3 Slider2D( Vector3 position, Vector3 normal, float size, Handles.CapFunction cap )
		{
			var e = Event.current;
			var id = ControlID( SliderHash );
			var result = position;
			var g = HammerGUI.WorldToGUI( position );

			if ( e.type == EventType.Layout )
				HandleUtility.AddControl( id, Mathf.Max( 0, Vector2.Distance( e.mousePosition, g ) - 8 - GrabMargin ) );

			if ( BeginDrag( id, e ) )
			{
				_startPosition = position;
				RayPlane( HammerGUI.GUIToRay( e.mousePosition ), normal, position, out _startHit );
			}

			if ( Dragging( id, e ) )
			{
				RayPlane( HammerGUI.GUIToRay( e.mousePosition ), normal, _startPosition, out var hit );
				result = _startPosition + Vector3.ProjectOnPlane( hit - _startHit, normal );
				GUI.changed = true;
				e.Use();
			}

			EndDrag( id, e );

			if ( e.type == EventType.Repaint )
			{
				var old = Handles.color;
				if ( GUIUtility.hotControl == id || HandleUtility.nearestControl == id ) Handles.color = HotColor;
				cap( id, position, Quaternion.identity, size, EventType.Repaint );
				Handles.color = old;
			}

			return result;
		}

		static float DistanceToCircle( Vector2 mouse, Vector3 center, Vector3 normal, float radius )
		{
			var tangent = Vector3.Cross( normal, Mathf.Abs( normal.y ) < 0.9f ? Vector3.up : Vector3.right ).normalized;
			var bitangent = Vector3.Cross( normal, tangent );
			var best = float.MaxValue;
			var prev = HammerGUI.WorldToGUI( center + tangent * radius );

			for ( int i = 1; i <= 48; i++ )
			{
				var a = i / 48.0f * Mathf.PI * 2;
				var p = HammerGUI.WorldToGUI( center + (tangent * Mathf.Cos( a ) + bitangent * Mathf.Sin( a )) * radius );
				if ( p.z > 0 && prev.z > 0 )
					best = Mathf.Min( best, HammerGUI.DistancePointSegment( mouse, prev, p ) );
				prev = p;
			}

			return best;
		}

		static float DistanceToQuad( Vector2 p, Vector3[] quad )
		{
			var best = float.MaxValue;
			for ( int i = 0; i < quad.Length; i++ )
				best = Mathf.Min( best, HammerGUI.DistanceToSegment( p, quad[i], quad[(i + 1) % quad.Length] ) );
			return best;
		}

		static bool InsideQuad( Vector2 p, Vector3[] quad )
		{
			var g = new Vector2[4];
			for ( int i = 0; i < 4; i++ ) g[i] = HammerGUI.WorldToGUI( quad[i] );

			bool inside = false;
			for ( int i = 0, j = 3; i < 4; j = i++ )
			{
				if ( (g[i].y > p.y) != (g[j].y > p.y) && p.x < (g[j].x - g[i].x) * (p.y - g[i].y) / (g[j].y - g[i].y) + g[i].x )
					inside = !inside;
			}

			return inside;
		}
	}
}
