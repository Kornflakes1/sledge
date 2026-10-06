using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Shift+X. Drag a line across a surface to define a cutting plane through it, then Enter to
	/// clip. Works on whole objects (object mode) or on the selected faces. Port of the s&amp;box clip tool.
	/// </summary>
	public sealed class ClipTool : SubTool
	{
		public enum KeepMode
		{
			Front,
			Back,
			Both,
		}

		sealed class Target
		{
			public HammerMesh Component;
			public S.PolygonMesh Original;
			public HashSet<FaceHandle> Faces; // null = whole mesh
		}

		static KeepMode _keep = KeepMode.Front;
		static bool _cap = true;

		readonly List<Target> _targets = new();
		bool _faceSelection;

		bool _hasHitPlane;
		Vector3 _hitNormal;
		Vector3 _point1;
		Vector3 _point2;
		bool _hasPlane;
		bool _dragging;
		readonly List<(Vector3, Vector3)> _newEdges = new();

		public override string Title => "Clipping Tool";
		public override string Help => "Drag across a surface to set the cut. Shift+X cycles which side is kept. Enter applies, Space applies and stays, Esc cancels.";

		public override (string Key, string Operation)[] Keys => new[]
		{
			("Enter", "Finish Clip"),
			("Space", "Apply Clip"),
			("Esc", "Cancel Clip"),
			("Shift+X", "Cycle Clipping Side"),
			("Ctrl+Shift+X", "Toggle Create Caps"),
			("G", "Rotate Plane CW"),
			("F", "Rotate Plane CCW"),
		};

		static readonly Color Cyan = new( 0.15f, 0.95f, 1.0f );

		public static void Open( HammerMeshTool tool )
		{
			var clip = new ClipTool();
			if ( clip.CollectTargets( tool ) )
				tool.BeginSubTool( clip );
		}

		bool CollectTargets( HammerMeshTool tool )
		{
			_targets.Clear();

			if ( tool.Mode == EditMode.Face && tool.SelectedFaces.Any() )
			{
				_faceSelection = true;
				foreach ( var g in tool.SelectedFaces.GroupBy( x => x.Component ) )
				{
					var faces = g.Select( x => x.Handle ).ToHashSet();
					_targets.Add( new Target
					{
						Component = g.Key,
						Original = g.Key.Mesh,
						Faces = faces.Count == g.Key.Mesh.FaceHandles.Count() ? null : faces,
					} );
				}
			}
			else
			{
				foreach ( var go in UnityEditor.Selection.gameObjects )
				{
					foreach ( var c in go.GetComponentsInChildren<HammerMesh>() )
						_targets.Add( new Target { Component = c, Original = c.Mesh } );
				}
			}

			return _targets.Count > 0;
		}

		public override void OnOverlayGUI()
		{
			GUILayout.Label( "Keep Mode", EditorStyles.miniLabel );
			using ( new GUILayout.HorizontalScope() )
			{
				GUILayout.FlexibleSpace();
				foreach ( var mode in new[] { KeepMode.Front, KeepMode.Back, KeepMode.Both } )
				{
					if ( KeepButton( mode ) && mode != _keep )
					{
						_keep = mode;
						Preview();
					}
					GUILayout.FlexibleSpace();
				}
			}
			GUILayout.Space( 4 );

			var cap = GUILayout.Toggle( _cap, "Cap new surfaces" );
			if ( cap != _cap )
			{
				_cap = cap;
				Preview();
			}
		}

		static readonly Color Kept = new( 0.2f, 0.8f, 0.25f );

		/// <summary>
		/// Hammer's keep mode icons: the cut down the middle, the kept half (or both) in green.
		/// </summary>
		static bool KeepButton( KeepMode mode )
		{
			var tip = mode switch { KeepMode.Front => "Keep the front", KeepMode.Back => "Keep the back", _ => "Keep both (split)" };
			var rect = GUILayoutUtility.GetRect( 34, 26, GUILayout.Width( 34 ), GUILayout.Height( 26 ) );
			var clicked = GUI.Button( rect, new GUIContent( "", tip ), EditorStyles.miniButton );

			if ( Event.current.type == EventType.Repaint )
			{
				if ( mode == _keep )
				{
					var border = new Color( 0.95f, 0.55f, 0.15f );
					EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, 1 ), border );
					EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), border );
					EditorGUI.DrawRect( new Rect( rect.x, rect.y, 1, rect.height ), border );
					EditorGUI.DrawRect( new Rect( rect.xMax - 1, rect.y, 1, rect.height ), border );
				}

				var gone = new Color( 0.5f, 0.5f, 0.5f, 0.5f );
				var left = new Rect( rect.center.x - 8, rect.y + 6, 6, rect.height - 12 );
				var right = new Rect( rect.center.x + 2, rect.y + 6, 6, rect.height - 12 );
				EditorGUI.DrawRect( left, mode == KeepMode.Front ? gone : Kept );
				EditorGUI.DrawRect( right, mode == KeepMode.Back ? gone : Kept );
				EditorGUI.DrawRect( new Rect( rect.center.x - 0.5f, rect.y + 4, 1, rect.height - 8 ), Color.white );
			}

			return clicked;
		}

		public void ToggleCaps()
		{
			_cap = !_cap;
			Preview();
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// G / F: turn the cut line about its middle, in the surface, by the angle snap.
		/// </summary>
		public void RotatePlane( int direction )
		{
			if ( !_hasHitPlane || (_point2 - _point1).sqrMagnitude < 1e-10f ) return;
			var mid = (_point1 + _point2) * 0.5f;
			var turn = Quaternion.AngleAxis( direction * HammerSettings.AngleSnap, _hitNormal );
			_point1 = mid + turn * (_point1 - mid);
			_point2 = mid + turn * (_point2 - mid);
			Preview();
			HammerViews.RepaintAll();
		}

		public void CycleKeepMode()
		{
			_keep = (KeepMode)(((int)_keep + 1) % 3);
			Preview();
			HammerViews.RepaintAll();
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			// The plane first, so the line and handles sit on top of it
			if ( e.type == EventType.Repaint )
				Draw();

			// Endpoint handles, draggable within the surface plane
			if ( _hasPlane && !_dragging )
			{
				EditorGUI.BeginChangeCheck();
				Handles.color = Cyan;
				var p1 = HammerGizmos.Slider2D( _point1, _hitNormal, HandleSize( _point1 ), Handle );
				var p2 = HammerGizmos.Slider2D( _point2, _hitNormal, HandleSize( _point2 ), Handle );
				if ( EditorGUI.EndChangeCheck() )
				{
					_point1 = SnapInPlane( p1 );
					_point2 = SnapInPlane( p2 );
					Preview();
				}
			}
			else if ( _hasHitPlane && e.type == EventType.Repaint )
			{
				Handles.color = Cyan;
				Handle( 0, _point1, Quaternion.identity, HandleSize( _point1 ), EventType.Repaint );
				Handle( 0, _point2, Quaternion.identity, HandleSize( _point2 ), EventType.Repaint );
			}

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 0 && !e.alt:
				{
					if ( !TraceSurface( e.mousePosition, out var point, out var normal ) )
						break;

					_hasHitPlane = true;
					_hitNormal = normal;
					_point1 = SnapInPlane( point );
					_point2 = _point1;
					_hasPlane = false;
					_dragging = true;
					GUIUtility.hotControl = id;
					e.Use();
					break;
				}

				case EventType.MouseDrag when _dragging && GUIUtility.hotControl == id:
				{
					var ray = HammerGUI.GUIToRay( e.mousePosition );
					var plane = new Plane( _hitNormal, _point1 );
					if ( plane.Raycast( ray, out var enter ) )
					{
						var p = SnapInPlane( ray.GetPoint( enter ) );
						if ( (p - _point1).sqrMagnitude > 1e-8f )
						{
							_point2 = p;
							Preview();
						}
					}
					e.Use();
					break;
				}

				case EventType.MouseUp when _dragging && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_dragging = false;
					e.Use();
					break;

				case EventType.KeyDown when e.keyCode == KeyCode.Space:
					ApplyInternal( false );
					e.Use();
					break;
			}
		}

		static float HandleSize( Vector3 p ) => HammerGUI.HandleSize( p ) * 0.045f;

		/// <summary>A cyan square with a dark rim, like Hammer's clip handles.</summary>
		static void Handle( int id, Vector3 position, Quaternion rotation, float size, EventType type )
		{
			if ( type != EventType.Repaint ) return;
			var color = Handles.color;
			Handles.color = new Color( 0.05f, 0.15f, 0.2f );
			Handles.DotHandleCap( id, position, rotation, size * 1.3f, type );
			Handles.color = color;
			Handles.DotHandleCap( id, position, rotation, size, type );
		}

		static Vector3 Perpendicular( Vector3 n ) => Mathf.Abs( Vector3.Dot( n, Vector3.up ) ) > 0.9f ? Vector3.right : Vector3.Cross( n, Vector3.up ).normalized;

		Vector3 SnapInPlane( Vector3 p )
		{
			if ( !HammerSettings.GridSnap ) return p;

			// Snap the two in-plane axes, leave the depth on the surface
			var axis = new Vector3( Mathf.Abs( _hitNormal.x ), Mathf.Abs( _hitNormal.y ), Mathf.Abs( _hitNormal.z ) );
			return HammerSettings.SnapWorld( p, axis.x < 0.5f, axis.y < 0.5f, axis.z < 0.5f );
		}

		bool TraceSurface( Vector2 mouse, out Vector3 point, out Vector3 normal )
		{
			var ray = HammerGUI.GUIToRay( mouse );

			if ( MeshPicking.RaycastFace( ray, MeshPicking.VisibleMeshes(), out var hit ) )
			{
				normal = NearestAxis( hit.Normal );
				point = hit.Point;
				return true;
			}

			// Otherwise a plane facing the camera, squared to the nearest axis
			normal = NearestAxis( -HammerGUI.Camera.transform.forward );
			var center = _targets.Count > 0 ? _targets[0].Component.transform.position : Vector3.zero;
			var plane = new Plane( normal, center );
			if ( plane.Raycast( ray, out var enter ) )
			{
				point = ray.GetPoint( enter );
				return true;
			}

			point = default;
			return false;
		}

		static Vector3 NearestAxis( Vector3 n )
		{
			var a = new Vector3( Mathf.Abs( n.x ), Mathf.Abs( n.y ), Mathf.Abs( n.z ) );
			if ( a.x >= a.y && a.x >= a.z ) return new Vector3( Mathf.Sign( n.x ), 0, 0 );
			if ( a.y >= a.z ) return new Vector3( 0, Mathf.Sign( n.y ), 0 );
			return new Vector3( 0, 0, Mathf.Sign( n.z ) );
		}

		/// <summary>
		/// The cutting plane in world space: contains the drawn line and the surface normal.
		/// </summary>
		bool TryGetPlane( out Vector3 point, out Vector3 normal )
		{
			point = _point1;
			normal = default;

			if ( !_hasHitPlane ) return false;

			var right = _point2 - _point1;
			if ( right.sqrMagnitude < 1e-10f ) return false;

			normal = Vector3.Cross( _hitNormal, right ).normalized;
			return normal.sqrMagnitude > 0.5f;
		}

		protected override void OnSettingsUndone() => Preview();

		void Preview()
		{
			_newEdges.Clear();
			_hasPlane = TryGetPlane( out var point, out var normal );

			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;

				if ( !_hasPlane )
				{
					t.Component.SetPreviewMesh( t.Original );
					continue;
				}

				var copy = Copy( t.Original, out var faceMap, out _ );
				var faces = t.Faces?.Where( faceMap.ContainsKey ).Select( f => faceMap[f] ).ToHashSet();
				Clip( t.Component, copy, point, normal, _keep == KeepMode.Both ? KeepMode.Both : _keep, faces, _newEdges );
				t.Component.SetPreviewMesh( copy );
			}

			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Clip a mesh by a world space plane, keeping one side (or splitting along it for Both).
		/// </summary>
		internal static void Clip( HammerMesh component, S.PolygonMesh mesh, Vector3 point, Vector3 normal, KeepMode keep, HashSet<FaceHandle> faces, List<(Vector3, Vector3)> newEdgeLines )
		{
			if ( keep == KeepMode.Front )
				normal = -normal;

			var localPoint = component.WorldToSource( point );
			var localNormal = SourceSpace.ToSourceDirection( component.transform.InverseTransformDirection( normal ) ).Normal;
			var plane = new S.Plane( localPoint, localNormal );

			var faceList = (faces ?? mesh.FaceHandles.ToHashSet()).ToList();
			var newEdges = new List<HalfEdgeHandle>();
			mesh.ClipFacesByPlaneAndCap( faceList, plane, keep != KeepMode.Both, _cap, newEdges );
			mesh.ComputeFaceTextureCoordinatesFromParameters();

			if ( newEdgeLines == null ) return;

			foreach ( var e in newEdges )
			{
				if ( !e.IsValid ) continue;
				var line = mesh.GetEdgeLine( e );
				newEdgeLines.Add( (component.SourceToWorld( line.Start ), component.SourceToWorld( line.End )) );
			}
		}

		/// <summary>
		/// Hammer's look: the cutting plane as a see-through sheet through the meshes, and the
		/// drawn line along the surface in cyan.
		/// </summary>
		void Draw()
		{
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			if ( !_hasHitPlane ) return;

			var line = _point2 - _point1;
			var length = line.magnitude;
			if ( length > 1e-5f )
			{
				var along = line / length;
				var up = _hitNormal;

				// Wide and tall enough to cover what's being cut, and a little more
				float a0 = 0, a1 = length, u0 = -length * 0.5f, u1 = length * 0.5f;
				var any = false;
				foreach ( var t in _targets )
				{
					var r = t.Component != null ? t.Component.GetComponent<Renderer>() : null;
					if ( r == null ) continue;
					var b = r.bounds;
					for ( int i = 0; i < 8; i++ )
					{
						var corner = b.center + Vector3.Scale( b.extents, new Vector3( (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1 ) );
						var d = corner - _point1;
						var a = Vector3.Dot( d, along );
						var u = Vector3.Dot( d, up );
						if ( !any ) { u0 = u1 = u; any = true; }
						a0 = Mathf.Min( a0, a );
						a1 = Mathf.Max( a1, a );
						u0 = Mathf.Min( u0, u );
						u1 = Mathf.Max( u1, u );
					}
				}
				var margin = Mathf.Max( a1 - a0, u1 - u0 ) * 0.08f;
				a0 -= margin; a1 += margin; u0 -= margin; u1 += margin;
				var quad = new[]
				{
					_point1 + along * a0 + up * u0,
					_point1 + along * a1 + up * u0,
					_point1 + along * a1 + up * u1,
					_point1 + along * a0 + up * u1,
				};
				Handles.color = Color.white;
				Handles.DrawSolidRectangleWithOutline( quad, new Color( 0.85f, 0.92f, 0.95f, 0.22f ), new Color( 1, 1, 1, 0.6f ) );
			}

			Handles.color = Cyan;
			Handles.DrawAAPolyLine( 4.0f, _point1, _point2 );
		}

		public override void Apply() => ApplyInternal( true );

		/// <summary>
		/// Space: apply the cut and keep the tool open for another.
		/// </summary>
		public void ApplyAndStay() => ApplyInternal( false );

		void ApplyInternal( bool close )
		{
			if ( !TryGetPlane( out var point, out var normal ) )
			{
				if ( close ) Cancel();
				return;
			}

			var components = _targets.Select( x => x.Component ).Where( x => x != null ).ToArray();

			// Put the originals back so undo records the state before the clip
			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Undo.RecordObjects( components, "Clip" );
			var created = new List<GameObject>();

			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;

				if ( !_faceSelection && _keep == KeepMode.Both )
				{
					// Split into two objects: this one keeps the front, a copy keeps the back
					var copyGo = Object.Instantiate( t.Component.gameObject, t.Component.transform.parent );
					copyGo.name = GameObjectUtility.GetUniqueNameForSibling( t.Component.transform.parent, t.Component.name );
					Undo.RegisterCreatedObjectUndo( copyGo, "Clip" );
					var copy = copyGo.GetComponent<HammerMesh>();
					var back = S.PolygonMesh.FromData( t.Original.ToData() );
					Clip( copy, back, point, normal, KeepMode.Back, null, null );
					copy.Mesh = back;
					created.Add( copyGo );

					Clip( t.Component, t.Original, point, normal, KeepMode.Front, null, null );
				}
				else
				{
					Clip( t.Component, t.Original, point, normal, _keep, t.Faces, null );
				}

				t.Component.Commit();
				EditorUtility.SetDirty( t.Component );
			}

			Tool.Selection.Clear();

			if ( created.Count > 0 )
				UnityEditor.Selection.objects = UnityEditor.Selection.objects.Concat( created ).ToArray();

			if ( close )
			{
				Close();
			}
			else
			{
				// Space: stay in the tool for another cut
				foreach ( var t in _targets )
					if ( t.Component != null ) t.Original = t.Component.Mesh;
				_hasHitPlane = false;
				_hasPlane = false;
				_newEdges.Clear();
			}
		}

		public override void Cancel()
		{
			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Close();
		}
	}
}
