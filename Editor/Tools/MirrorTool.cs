using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Shift+F. Drag a line across a surface to define a mirror plane; a mirrored copy of the
	/// selected objects (or faces) follows the plane. Enter keeps it. Port of the s&amp;box mirror tool.
	/// </summary>
	public sealed class MirrorTool : SubTool
	{
		readonly List<(GameObject copy, Vector3 position, Quaternion rotation)> _copies = new();

		bool _hasHitPlane;
		Vector3 _hitNormal;
		Vector3 _point1;
		Vector3 _point2;
		bool _dragging;
		int _undoGroup;

		// Faces mode: the meshes the mirrored faces go back into (Hammer keeps them in the same mesh)
		readonly Dictionary<GameObject, HammerMesh> _mergeInto = new();

		public override string Title => "Mirror";
		public override string Help => "Drag across a surface to place the mirror plane. Enter keeps the copy, Esc cancels.";

		public static void Open( HammerMeshTool tool )
		{
			var mirror = new MirrorTool();
			Undo.IncrementCurrentGroup();
			mirror._undoGroup = Undo.GetCurrentGroup();
			Undo.SetCurrentGroupName( "Mirror" );

			if ( tool.Mode == EditMode.Face && tool.SelectedFaces.Any() )
			{
				foreach ( var g in tool.SelectedFaces.GroupBy( x => x.Component ) )
				{
					var keep = g.Select( x => x.Index ).ToHashSet();
					var copy = CreateCopy( g.Key, keep );
					mirror._copies.Add( (copy.gameObject, g.Key.transform.position, g.Key.transform.rotation) );
					mirror._mergeInto[copy.gameObject] = g.Key;
				}
			}
			else
			{
				foreach ( var go in UnityEditor.Selection.gameObjects )
				{
					foreach ( var c in go.GetComponentsInChildren<HammerMesh>() )
					{
						var copy = CreateCopy( c, null );
						mirror._copies.Add( (copy.gameObject, c.transform.position, c.transform.rotation) );
					}
				}
			}

			if ( mirror._copies.Count == 0 )
				return;

			// Hidden until a plane is drawn
			foreach ( var (copy, _, _) in mirror._copies )
				copy.SetActive( false );

			tool.BeginSubTool( mirror );
		}

		/// <summary>
		/// A copy with its geometry reflected across the local s&amp;box Y axis (Unity X), so a
		/// regular rotation can place it as a mirror image.
		/// </summary>
		static HammerMesh CreateCopy( HammerMesh source, HashSet<int> keepFaces )
		{
			var go = Object.Instantiate( source.gameObject, source.transform.parent );
			go.name = GameObjectUtility.GetUniqueNameForSibling( source.transform.parent, source.name );
			Undo.RegisterCreatedObjectUndo( go, "Mirror" );

			var c = go.GetComponent<HammerMesh>();
			var mesh = S.PolygonMesh.FromData( source.Mesh.ToData() );

			if ( keepFaces != null )
				mesh.RemoveFaces( mesh.FaceHandles.Where( f => !keepFaces.Contains( f.Index ) ).ToList() );

			mesh.FlipAllFaces();
			mesh.Scale( new S.Vector3( 1, -1, 1 ) );
			mesh.ComputeFaceTextureCoordinatesFromParameters();
			c.Mesh = mesh;
			return c;
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 0 && !e.alt:
				{
					var ray = HammerGUI.GUIToRay( e.mousePosition );
					var meshes = MeshPicking.VisibleMeshes().Where( m => _copies.All( c => c.copy != m.gameObject ) );
					Vector3 point;

					if ( MeshPicking.RaycastFace( ray, meshes, out var hit ) )
					{
						_hitNormal = NearestAxis( hit.Normal );
						point = hit.Point;
					}
					else
					{
						_hitNormal = Vector3.up;
						if ( !new Plane( Vector3.up, Vector3.zero ).Raycast( ray, out var enter ) ) break;
						point = ray.GetPoint( enter );
					}

					_hasHitPlane = true;
					_point1 = Snap( point );
					_point2 = _point1;
					_dragging = true;
					GUIUtility.hotControl = id;
					e.Use();
					break;
				}

				case EventType.MouseDrag when _dragging && GUIUtility.hotControl == id:
				{
					var ray = HammerGUI.GUIToRay( e.mousePosition );
					if ( new Plane( _hitNormal, _point1 ).Raycast( ray, out var enter ) )
					{
						_point2 = Snap( ray.GetPoint( enter ) );
						UpdateCopies();
					}
					e.Use();
					break;
				}

				case EventType.MouseUp when _dragging && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_dragging = false;
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint && _hasHitPlane )
			{
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
				Handles.color = Color.white;
				Handles.DrawAAPolyLine( 4.0f, _point1, _point2 );
			}
		}

		Vector3 Snap( Vector3 p )
		{
			if ( !HammerSettings.GridSnap ) return p;
			var a = new Vector3( Mathf.Abs( _hitNormal.x ), Mathf.Abs( _hitNormal.y ), Mathf.Abs( _hitNormal.z ) );
			return HammerSettings.SnapWorld( p, a.x < 0.5f, a.y < 0.5f, a.z < 0.5f );
		}

		static Vector3 NearestAxis( Vector3 n )
		{
			var a = new Vector3( Mathf.Abs( n.x ), Mathf.Abs( n.y ), Mathf.Abs( n.z ) );
			if ( a.x >= a.y && a.x >= a.z ) return new Vector3( Mathf.Sign( n.x ), 0, 0 );
			if ( a.y >= a.z ) return new Vector3( 0, Mathf.Sign( n.y ), 0 );
			return new Vector3( 0, 0, Mathf.Sign( n.z ) );
		}

		bool TryGetPlane( out Vector3 normal )
		{
			normal = default;
			var right = _point2 - _point1;
			if ( !_hasHitPlane || right.sqrMagnitude < 1e-10f ) return false;
			normal = Vector3.Cross( _hitNormal, right ).normalized;
			return true;
		}

		protected override void OnSettingsUndone() => UpdateCopies();

		void UpdateCopies()
		{
			if ( !TryGetPlane( out var n ) )
			{
				foreach ( var (copy, _, _) in _copies ) copy.SetActive( false );
				return;
			}

			Vector3 Reflect( Vector3 v ) => v - 2.0f * Vector3.Dot( n, v ) * n;

			foreach ( var (copy, position, rotation) in _copies )
			{
				if ( copy == null ) continue;
				copy.SetActive( true );

				var d = Vector3.Dot( position - _point1, n );
				copy.transform.position = position - 2.0f * d * n;
				copy.transform.rotation = Quaternion.LookRotation( Reflect( rotation * Vector3.forward ), Reflect( rotation * Vector3.up ) );
			}

			HammerViews.RepaintAll();
		}

		public override void Apply()
		{
			if ( !TryGetPlane( out _ ) )
			{
				Cancel();
				return;
			}

			var kept = new List<Object>();
			Tool.Selection.Clear();

			foreach ( var (copy, _, _) in _copies )
			{
				if ( copy == null ) continue;
				var c = copy.GetComponent<HammerMesh>();
				c.Mesh.SetTransform( c.WorldTransform );
				c.Commit();

				// Faces mode: the mirrored faces join the mesh they came from, selected
				if ( _mergeInto.TryGetValue( copy, out var target ) && target != null )
				{
					Undo.RecordObject( target, "Mirror" );
					var relative = target.WorldTransform.ToLocal( c.WorldTransform );
					target.Mesh.MergeMesh( c.Mesh, relative, out _, out _, out var faces );
					target.Mesh.ComputeFaceTextureCoordinatesFromParameters();
					target.Commit();
					EditorUtility.SetDirty( target );
					Undo.DestroyObjectImmediate( copy );
					if ( faces != null )
						foreach ( var f in faces.Values ) Tool.Selection.Add( new MeshFace( target, f ) );
					kept.Add( target.gameObject );
					continue;
				}

				kept.Add( copy );
			}

			Undo.CollapseUndoOperations( _undoGroup );
			UnityEditor.Selection.objects = kept.ToArray();
			Close();
		}

		public override void Cancel()
		{
			// Throw away the copies, and their undo entries with them
			Undo.RevertAllDownToGroup( _undoGroup );

			foreach ( var (copy, _, _) in _copies )
				if ( copy != null ) Object.DestroyImmediate( copy );

			_copies.Clear();
			Close();
		}
	}
}
