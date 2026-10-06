using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's polygon tool: click out the corners of a shape on a surface (or the grid in a 2D
	/// view), close it, then set its height. Makes a new mesh: the shape extruded into a prism.
	/// </summary>
	public sealed class PolygonTool : SubTool
	{
		enum Stage { Points, Height }

		static float _depth = 64 * SourceSpace.UnitScale;

		readonly List<Vector3> _points = new();
		Vector3 _normal;
		Plane _plane;
		Stage _stage;
		float _height;
		Vector2 _heightMouseStart;
		Vector3 _hover;
		bool _hasHover;

		public override string Title => "Polygon Tool";

		public override string Help => _stage == Stage.Points
			? "Click to add points, click the first point (or Enter) to close. Backspace removes the last point."
			: "Move the mouse to set the height, click or Enter to create.";

		public static void Open( HammerMeshTool tool ) => tool.BeginSubTool( new PolygonTool() );

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			var grid = HammerSettings.GridSize * SourceSpace.UnitScale;

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseMove:
				case EventType.MouseDrag:
					if ( _stage == Stage.Points )
					{
						_hasHover = PointUnder( e.mousePosition, out _hover );
					}
					else
					{
						_height = HeightFromMouse( e.mousePosition, grid );
						if ( HammerSettings.GridSnap ) _height = Mathf.Round( _height / grid ) * grid;
						if ( Mathf.Approximately( _height, 0 ) ) _height = grid;
					}
					view.Repaint();
					break;

				case EventType.MouseDown when e.button == 0 && !e.alt:
				{
					if ( _stage == Stage.Height )
					{
						Apply();
						e.Use();
						break;
					}

					// Clicking the first point again closes the shape
					if ( _points.Count >= 3 && Vector2.Distance( HammerGUI.WorldToGUI2( _points[0] ), e.mousePosition ) < 10 )
					{
						FinishShape( view );
						e.Use();
						break;
					}

					if ( PointUnder( e.mousePosition, out var p ) )
					{
						if ( _points.Count == 0 && !StartPlane( e.mousePosition, out p ) )
							break;

						if ( _points.Count == 0 || Vector3.Distance( p, _points[^1] ) > 1e-4f )
							_points.Add( p );
					}

					e.Use();
					view.Repaint();
					break;
				}

				case EventType.KeyDown when e.keyCode == KeyCode.Backspace && _stage == Stage.Points:
					if ( _points.Count > 0 ) _points.RemoveAt( _points.Count - 1 );
					e.Use();
					view.Repaint();
					break;
			}

			if ( e.type == EventType.Repaint )
				Draw();
		}

		/// <summary>
		/// The first click picks the plane: the surface under the mouse (squared to an axis), or
		/// the grid plane facing a 2D view.
		/// </summary>
		bool StartPlane( Vector2 mouse, out Vector3 point )
		{
			if ( !Tool.TryGetPlacementPoint( mouse, out point, out _normal ) )
				return false;

			point = Snap( point );
			_plane = new Plane( _normal, point );
			return true;
		}

		bool PointUnder( Vector2 mouse, out Vector3 point )
		{
			point = default;
			if ( _points.Count == 0 )
			{
				if ( !Tool.TryGetPlacementPoint( mouse, out point, out _ ) ) return false;
				point = Snap( point );
				return true;
			}

			var ray = HammerGUI.GUIToRay( mouse );
			if ( !_plane.Raycast( ray, out var enter ) && !new Plane( -_plane.normal, -_plane.distance ).Raycast( ray, out enter ) )
				return false;

			point = Snap( ray.GetPoint( enter ) );
			// Keep it exactly on the plane after snapping
			point -= _plane.normal * _plane.GetDistanceToPoint( point );
			return true;
		}

		static Vector3 Snap( Vector3 world ) => HammerSettings.GridSnap ? HammerSettings.SnapWorld( world ) : world;

		void FinishShape( HammerView view )
		{
			if ( _points.Count < 3 ) return;

			// 2D views have no height stage: use the depth of the last shape, like the block tool
			if ( view.Orthographic )
			{
				_stage = Stage.Height;
				_height = _depth;
				Apply();
				return;
			}

			_stage = Stage.Height;
			_height = HammerSettings.GridSize * SourceSpace.UnitScale * 8;
			_heightMouseStart = Event.current.mousePosition;
		}

		float HeightFromMouse( Vector2 mouse, float grid )
		{
			var ray = HammerGUI.GUIToRay( mouse );
			var origin = Centroid();
			var axis = _normal;

			var w = origin - ray.origin;
			var b = Vector3.Dot( axis, ray.direction );
			var denom = 1.0f - b * b;
			float height;

			if ( denom > 0.02f )
				height = (b * Vector3.Dot( ray.direction, w ) - Vector3.Dot( axis, w )) / denom;
			else
				height = grid * 8 + (_heightMouseStart.y - mouse.y) / 10.0f * grid;

			var size = _points.Max( p => Vector3.Distance( p, origin ) ) * 2;
			var limit = Mathf.Max( grid * 64, size * 4 );
			return Mathf.Clamp( height, -limit, limit );
		}

		Vector3 Centroid()
		{
			var c = Vector3.zero;
			foreach ( var p in _points ) c += p;
			return c / Mathf.Max( 1, _points.Count );
		}

		void Draw()
		{
			var zTest = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			var yellow = new Color( 1.0f, 0.92f, 0.15f );

			if ( _stage == Stage.Points )
			{
				Handles.color = yellow;
				if ( _points.Count > 1 ) Handles.DrawAAPolyLine( 3, _points.ToArray() );

				if ( _hasHover && _points.Count > 0 )
				{
					Handles.color = new Color( 1.0f, 0.92f, 0.15f, 0.5f );
					Handles.DrawDottedLine( _points[^1], _hover, 4 );
					if ( _points.Count >= 2 ) Handles.DrawDottedLine( _hover, _points[0], 4 );
				}

				foreach ( var p in _points )
				{
					Handles.color = yellow;
					Handles.DotHandleCap( 0, p, Quaternion.identity, HammerGUI.HandleSize( p ) * 0.04f, EventType.Repaint );
				}

				if ( _hasHover )
				{
					Handles.color = new Color( 0.4f, 0.9f, 1.0f );
					Handles.DotHandleCap( 0, _hover, Quaternion.identity, HammerGUI.HandleSize( _hover ) * 0.035f, EventType.Repaint );
				}
			}
			else
			{
				var offset = _normal * _height;
				Handles.color = yellow;
				var bottom = _points.Append( _points[0] ).ToArray();
				var top = bottom.Select( p => p + offset ).ToArray();
				Handles.DrawAAPolyLine( 3, bottom );
				Handles.DrawAAPolyLine( 3, top );
				foreach ( var p in _points ) Handles.DrawAAPolyLine( 2, p, p + offset );

				var units = Mathf.Abs( _height ) / SourceSpace.UnitScale;
				HammerGUI.OutlinedLabel( Centroid() + offset, $"{units:0.##}", Color.white );
			}

			Handles.zTest = zTest;
		}

		public override void Apply()
		{
			if ( _points.Count < 3 )
			{
				Cancel();
				return;
			}

			if ( _stage == Stage.Points )
			{
				// Enter while drawing closes the shape
				// (in a 2D view that creates the shape straight away)
				if ( HammerViews.Current != null ) FinishShape( HammerViews.Current );
				return;
			}

			_depth = Mathf.Abs( _height );
			Create();
			Close();
		}

		public override void Cancel() => Close();

		void Create()
		{
			var component = CreatePrism( _points, _normal, _height );
			UnityEditor.Selection.activeGameObject = component.gameObject;
			Tool.Selection.Clear();
		}

		/// <summary>
		/// A new mesh object: the polygon (world points on a plane) extruded along the normal by
		/// the height (world units). Undoable.
		/// </summary>
		public static HammerMesh CreatePrism( IList<Vector3> points, Vector3 planeNormal, float worldHeight, string name = "Polygon" )
		{
			// Work in s&box space: Z-up, inches, right handed (faces wind counter-clockwise
			// around their normal)
			var normal = SourceSpace.ToSourceDirection( planeNormal ).Normal;
			var basePoints = points.Select( SourceSpace.ToSourcePosition ).ToList();
			var height = worldHeight / SourceSpace.UnitScale;

			if ( height < 0 )
			{
				basePoints = basePoints.Select( p => p + normal * height ).ToList();
				height = -height;
			}

			// Wind the base counter-clockwise around the normal
			var c = basePoints.Aggregate( S.Vector3.Zero, ( a, p ) => a + p ) / basePoints.Count;
			var area = S.Vector3.Zero;
			for ( int i = 0; i < basePoints.Count; i++ )
				area += S.Vector3.Cross( basePoints[i] - c, basePoints[(i + 1) % basePoints.Count] - c );
			if ( S.Vector3.Dot( area, normal ) < 0 ) basePoints.Reverse();

			var topPoints = basePoints.Select( p => p + normal * height ).ToList();
			var center = (c + c + normal * height) * 0.5f;

			var mesh = new S.PolygonMesh();
			var n = basePoints.Count;
			var bottom = mesh.AddVertices( basePoints.Select( p => p - center ).ToArray() );
			var top = mesh.AddVertices( topPoints.Select( p => p - center ).ToArray() );
			var material = HammerMaterials.Get( HammerSettings.ActiveMaterial ) ?? S.Material.Load( HammerMaterials.DefaultKey );

			void Face( params HalfEdgeMesh.VertexHandle[] v ) => mesh.SetFaceMaterial( mesh.AddFace( v ), material );

			Face( top.ToArray() );
			Face( bottom.Reverse().ToArray() );
			for ( int i = 0; i < n; i++ )
			{
				var j = (i + 1) % n;
				Face( bottom[i], bottom[j], top[j], top[i] );
			}

			mesh.SetSmoothingAngle( 40.0f );

			var go = new GameObject( name );
			Undo.RegisterCreatedObjectUndo( go, "Create Polygon" );
			go.transform.position = SourceSpace.ToUnityPosition( center );
			go.isStatic = true;

			var component = go.AddComponent<HammerMesh>();
			component.SmoothingAngle = 40.0f;
			component.Mesh = mesh;
			// The mesh was built relative to the object's origin; keep its textures world aligned
			component.Mesh.TextureAlignToGrid( new S.Transform( center ) );
			component.Commit();
			return component;
		}
	}
}
