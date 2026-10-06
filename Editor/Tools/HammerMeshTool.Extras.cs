using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using PolygonMesh = Sandbox.PolygonMesh;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// The rest of Hammer's per-mode options: merge within a distance, edge extrude, UV peel,
	/// subdivision levels, filtered selection, and the Meshes mode origin, pivot and align tools.
	/// </summary>
	partial class HammerMeshTool
	{
		// Where the mouse last was over a view, for the keys that act "under the cursor"
		Vector2 _lastMouse;
		HammerView _lastMouseView;

		/// <summary>
		/// Space while a shape is being drawn confirms it; in the clipping tool it applies and
		/// stays. Returns false when Space should cycle the selection mode instead.
		/// </summary>
		public bool OnSpace()
		{
			if ( _subTool is ClipTool clip ) { clip.ApplyAndStay(); return true; }
			if ( _subTool is MeshProjectionTool projection ) { projection.Project(); return true; }
			if ( _subTool != null ) return true;
			if ( _mode == EditMode.Primitive ) { ConfirmPrimitive(); return true; }
			return false;
		}

		/// <summary>
		/// Ctrl+M: the material on the face under the cursor becomes the active one.
		/// </summary>
		public void LiftMaterialUnderCursor()
		{
			if ( _lastMouseView == null ) return;
			var previous = HammerViews.Current;
			HammerViews.Current = _lastMouseView;
			try { LiftMaterial( _lastMouse ); }
			finally { HammerViews.Current = previous; }
		}

		bool _pickingWorkplane;

		/// <summary>
		/// Shift+Q: the next click on a face sets the workplane there; Esc goes back to the world.
		/// </summary>
		public void BeginWorkplanePick()
		{
			_pickingWorkplane = true;
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// While picking a workplane, the view's clicks and Esc go here. Returns true if it took the event.
		/// </summary>
		bool WorkplanePickGUI( HammerView view )
		{
			if ( !_pickingWorkplane ) return false;
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout ) HandleUtility.AddDefaultControl( id );
			EditorGUIUtility.AddCursorRect( new Rect( 0, 0, 100000, 100000 ), MouseCursor.ArrowPlus );

			if ( e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape )
			{
				_pickingWorkplane = false;
				Workplane.Reset();
				e.Use();
				return true;
			}

			if ( e.type == EventType.MouseDown && e.button == 0 && !e.alt )
			{
				if ( MeshPicking.PickFace( e.mousePosition, MeshPicking.VisibleMeshes(), out var hit ) )
				{
					Workplane.SetFromFace( hit.Face );
					_pickingWorkplane = false;
				}
				e.Use();
				return true;
			}

			return false;
		}

		void TrackMouse( HammerView view )
		{
			var e = Event.current;
			if ( e.type is EventType.MouseMove or EventType.MouseDrag )
			{
				_lastMouse = e.mousePosition;
				_lastMouseView = view;

				// The mesh under the mouse: the only one (with the selected ones) whose edges the
				// 3D views draw
				var hover = !view.Orthographic && MeshPicking.PickFace( e.mousePosition, MeshPicking.VisibleMeshes(), out var hit ) ? hit.Face.Component : null;
				if ( hover != HoveredMesh )
				{
					HoveredMesh = hover;
					HammerViews.RepaintAll();
				}
			}
			else if ( e.type == EventType.MouseLeaveWindow && HoveredMesh != null )
			{
				HoveredMesh = null;
				HammerViews.RepaintAll();
			}
		}

		/// <summary>
		/// The mesh under the mouse in a 3D view (null over nothing or in a 2D view).
		/// </summary>
		public HammerMesh HoveredMesh { get; private set; }

		// ─────────────────────────────── Vertices ───────────────────────────────

		/// <summary>
		/// Merge with Range set to a distance: only selected vertices within it of each other join.
		/// </summary>
		public void MergeVerticesWithinDistance()
		{
			var groups = SelectedVertices.GroupBy( x => x.Component ).ToList();
			if ( groups.Count == 0 ) return;

			using ( Scope( "Merge Vertices" ) )
			{
				Selection.Clear();
				foreach ( var g in groups )
				{
					var mesh = g.Key.Mesh;
					mesh.MergeVerticesWithinDistance( g.Select( x => x.Handle ).ToList(), HammerSettings.MergeDistance, false, true, out var final );
					if ( final != null )
						foreach ( var v in final ) if ( v.IsValid ) Selection.Add( new MeshVertex( g.Key, v ) );
					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		// ─────────────────────────────── Edges ───────────────────────────────

		/// <summary>
		/// Extrude the selected edges one grid step: open edges outward along their face, others
		/// out from the surface (the same as starting a Shift+drag).
		/// </summary>
		public void ExtrudeEdges()
		{
			var edges = SelectedEdges.ToList();
			if ( edges.Count == 0 ) return;

			var direction = Vector3.zero;
			foreach ( var e in edges )
			{
				var mesh = e.Component.Mesh;
				mesh.GetFacesConnectedToEdge( e.Handle, out var a, out var b );
				var line = mesh.GetEdgeLine( e.Handle );

				if ( a.IsValid && b.IsValid )
				{
					mesh.ComputeFaceNormal( a, out var na );
					mesh.ComputeFaceNormal( b, out var nb );
					direction += e.Component.SourceDirectionToWorld( na + nb ).normalized;
				}
				else
				{
					// Out from the one face, in its plane
					var face = a.IsValid ? a : b;
					if ( !face.IsValid ) continue;
					mesh.ComputeFaceNormal( face, out var n );
					var along = (line.End - line.Start).Normal;
					var outward = S.Vector3.Cross( along, n ).Normal;
					if ( S.Vector3.Dot( outward, line.Center - mesh.GetFaceCenter( face ) ) < 0 ) outward = -outward;
					direction += e.Component.SourceDirectionToWorld( outward ).normalized;
				}
			}

			if ( direction.sqrMagnitude < 1e-8f ) direction = Vector3.up;
			var delta = direction.normalized * Grid * SourceSpace.UnitScale;
			Remember( "Extrude", () => RepeatDrag( true, _ => ApplyTranslate( delta ) ) );
			RepeatDrag( true, _ => ApplyTranslate( delta ) );
		}

		/// <summary>
		/// UV Peel: lay the texture along the strip of faces on the selected edges (an arch's
		/// underside, a pipe, a road), as one continuous run instead of each face on its own.
		/// </summary>
		public void PeelUVs()
		{
			var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
			if ( groups.Count == 0 ) return;

			using ( Scope( "UV Peel" ) )
			{
				foreach ( var g in groups )
					Peel( g.Key.Mesh, g.Select( x => x.Handle ).ToList() );
			}

			Done();
		}

		static float LineLength( S.Line line ) => (line.End - line.Start).Length;

		static (int, int) Key( PolygonMesh mesh, HalfEdgeHandle h )
		{
			mesh.GetEdgeVertices( h, out var a, out var b );
			return a.Index < b.Index ? (a.Index, b.Index) : (b.Index, a.Index);
		}

		static void Peel( PolygonMesh mesh, List<HalfEdgeHandle> rails )
		{
			var railKeys = rails.Select( h => Key( mesh, h ) ).ToHashSet();

			// The quads along the selected edges, and for each, which of its edges is the rail
			var faceRail = new Dictionary<int, int>(); // face index -> index of the rail in its edge list
			var faces = new Dictionary<int, FaceHandle>();
			foreach ( var h in rails )
			{
				mesh.GetFacesConnectedToEdge( h, out var a, out var b );
				foreach ( var f in new[] { a, b } )
				{
					if ( !f.IsValid || mesh.IsFaceHidden( f ) || faces.ContainsKey( f.Index ) ) continue;
					var edges = mesh.GetFaceEdges( f );
					if ( edges.Length != 4 ) continue;
					var r = System.Array.FindIndex( edges, e => railKeys.Contains( Key( mesh, e ) ) );
					if ( r < 0 ) continue;
					faces[f.Index] = f;
					faceRail[f.Index] = r;
				}
			}

			// Neighbours across the ribs (the edges either side of the rail)
			var ribFaces = new Dictionary<(int, int), List<int>>();
			foreach ( var (fi, f) in faces )
			{
				var edges = mesh.GetFaceEdges( f );
				var r = faceRail[fi];
				foreach ( var rib in new[] { edges[(r + 1) % 4], edges[(r + 3) % 4] } )
				{
					var k = Key( mesh, rib );
					if ( !ribFaces.TryGetValue( k, out var list ) ) ribFaces[k] = list = new List<int>();
					list.Add( fi );
				}
			}

			var visited = new HashSet<int>();
			var size = 128.0f; // world space: one texture repeat per 128 units (a 512 texture at 0.25)

			foreach ( var start in faces.Keys.OrderBy( fi => Neighbours( fi ).Count() ) )
			{
				if ( visited.Contains( start ) ) continue;

				// Walk the strip from an end (or anywhere, round a loop)
				var chain = new List<(int Face, (int, int) RibIn, (int, int) RibOut)>();
				var current = start;
				(int, int) ribIn = default;
				var first = true;
				while ( current >= 0 && visited.Add( current ) )
				{
					var edges = mesh.GetFaceEdges( faces[current] );
					var r = faceRail[current];
					var ribA = Key( mesh, edges[(r + 1) % 4] );
					var ribB = Key( mesh, edges[(r + 3) % 4] );

					if ( first )
					{
						// Leave by the side that has a neighbour, if either does
						var hasB = ribFaces[ribB].Any( x => x != current && !visited.Contains( x ) );
						ribIn = hasB ? ribA : ribB;
						first = false;
					}

					var ribOut = ribIn.Equals( ribA ) ? ribB : ribA;
					chain.Add( (current, ribIn, ribOut) );
					var candidates = ribFaces[ribOut].Where( x => x != current && !visited.Contains( x ) ).ToList();
					var next = candidates.Count > 0 ? candidates[0] : -1;
					ribIn = ribOut;
					current = next;
				}

				// Distance along the strip at each rib: the length of each face's rail
				var lengths = chain.Select( c => { var e = mesh.GetFaceEdges( faces[c.Face] )[faceRail[c.Face]]; return LineLength( mesh.GetEdgeLine( e ) ); } ).ToList();
				var total = Mathf.Max( lengths.Sum(), 1e-4f );

				var along = 0.0f;
				for ( int i = 0; i < chain.Count; i++ )
				{
					var (fi, rIn, rOut) = chain[i];
					var face = faces[fi];
					var railKey = Key( mesh, mesh.GetFaceEdges( face )[faceRail[fi]] );
					var width = (LineLength( mesh.GetEdgeLine( mesh.GetFaceEdges( face )[(faceRail[fi] + 1) % 4] ) ) + LineLength( mesh.GetEdgeLine( mesh.GetFaceEdges( face )[(faceRail[fi] + 3) % 4] ) )) * 0.5f;

					mesh.GetFaceVerticesConnectedToFace( face, out var corners );
					foreach ( var corner in corners )
					{
						var v = mesh.GetVertexConnectedToFaceVertex( corner ).Index;
						var u = (v == rIn.Item1 || v == rIn.Item2) ? along : along + lengths[i];
						var across = (v == railKey.Item1 || v == railKey.Item2) ? 0.0f : 1.0f;

						float uu, vv;
						if ( HammerSettings.PeelWorldSpace )
						{
							uu = u / size;
							vv = across * width / size;
						}
						else
						{
							uu = u / total * HammerSettings.PeelURepeats;
							vv = across * HammerSettings.PeelVRepeats;
						}

						var uv = HammerSettings.PeelAlongV ? new S.Vector2( vv, uu ) : new S.Vector2( uu, vv );
						mesh.SetTextureCoord( corner, uv + new S.Vector2( HammerSettings.PeelUOffset, HammerSettings.PeelVOffset ) );
					}

					along += lengths[i];
				}

				mesh.ComputeFaceTextureParametersFromCoordinates( chain.Select( c => faces[c.Face] ) );
			}

			IEnumerable<int> Neighbours( int fi )
			{
				var edges = mesh.GetFaceEdges( faces[fi] );
				var r = faceRail[fi];
				foreach ( var rib in new[] { edges[(r + 1) % 4], edges[(r + 3) % 4] } )
					foreach ( var other in ribFaces[Key( mesh, rib )] )
						if ( other != fi ) yield return other;
			}
		}

		// ─────────────────────────────── Faces ───────────────────────────────

		/// <summary>
		/// The meshes that subdivision buttons act on: the selected objects, or the meshes with
		/// something selected.
		/// </summary>
		List<HammerMesh> SubdivisionMeshes() => _mode == EditMode.Object ? SelectedObjectMeshes().ToList() : Selection.Components.ToList();

		public void SetSubdivision( int level ) => ChangeSubdivision( _ => level, $"Subdivision Level {level}" );
		public void IncreaseSubdivision() => ChangeSubdivision( l => l + 1, "Increase Subdivision" );
		public void DecreaseSubdivision() => ChangeSubdivision( l => l - 1, "Decrease Subdivision" );

		void ChangeSubdivision( System.Func<int, int> change, string name )
		{
			var meshes = SubdivisionMeshes();
			if ( meshes.Count == 0 ) return;

			Undo.RecordObjects( meshes.ToArray(), name );
			foreach ( var c in meshes )
			{
				c.SubdivisionLevel = change( c.SubdivisionLevel );
				EditorUtility.SetDirty( c );
			}
			Done();
		}

		/// <summary>
		/// Make the smoothed shape the real, editable mesh.
		/// </summary>
		public void BakeSubdivision()
		{
			var meshes = SubdivisionMeshes().Where( c => c.SubdivisionLevel > 0 ).ToList();
			if ( meshes.Count == 0 ) return;

			using ( Scope( "Bake Subdivision", meshes ) )
			{
				Selection.Clear();
				foreach ( var c in meshes )
				{
					var level = c.SubdivisionLevel;
					c.SubdivisionLevel = 0;
					c.Mesh = c.Mesh.Subdivided( level );
				}
			}

			Done();
		}

		/// <summary>
		/// Select Contiguous Element: spread from the selected faces to every face connected to
		/// them, stopping at the filters (same material, facing within the angle of its neighbour).
		/// </summary>
		public void SelectContiguous()
		{
			var seeds = SelectedFaces.ToList();
			if ( seeds.Count == 0 ) return;

			var cos = Mathf.Cos( HammerSettings.FilterNormalAngle * Mathf.Deg2Rad );
			var added = new List<MeshFace>();

			foreach ( var g in seeds.GroupBy( x => x.Component ) )
			{
				var mesh = g.Key.Mesh;
				var seen = new HashSet<int>( g.Select( x => x.Index ) );
				var queue = new Queue<FaceHandle>( g.Select( x => x.Handle ) );
				var material = g.First().Component.Mesh.GetFaceMaterial( g.First().Handle );

				while ( queue.Count > 0 )
				{
					var face = queue.Dequeue();
					mesh.ComputeFaceNormal( face, out var n );

					foreach ( var he in mesh.GetFaceEdges( face ) )
					{
						mesh.GetFacesConnectedToEdge( he, out var a, out var b );
						var next = a.Index == face.Index ? b : a;
						if ( !next.IsValid || mesh.IsFaceHidden( next ) || !seen.Add( next.Index ) ) continue;

						if ( HammerSettings.FilterMaterial && mesh.GetFaceMaterial( next ) != material ) continue;
						if ( HammerSettings.FilterNormal )
						{
							mesh.ComputeFaceNormal( next, out var nn );
							if ( S.Vector3.Dot( n, nn ) < cos ) continue;
						}

						added.Add( new MeshFace( g.Key, next ) );
						queue.Enqueue( next );
					}
				}
			}

			foreach ( var f in added ) Selection.Add( f );
			Done();
		}

		/// <summary>
		/// Faces mode Merge Meshes: join the objects the selected faces belong to.
		/// </summary>
		public void MergeSelectedFacesMeshes()
		{
			var meshes = Selection.Components.ToList();
			if ( meshes.Count < 2 ) return;

			UnityEditor.Selection.objects = meshes.Select( m => (Object)m.gameObject ).ToArray();
			UnityEditor.Selection.activeGameObject = meshes[0].gameObject;
			Selection.Clear();
			MergeMeshes();
		}

		/// <summary>
		/// Swap one material for another on faces: the selected faces, the selected objects, or
		/// everything in the scene. Returns how many faces changed.
		/// </summary>
		public static int ReplaceMaterial( IEnumerable<(HammerMesh Component, IEnumerable<FaceHandle> Faces)> targets, Material from, Material to )
		{
			var fromKey = from != null ? HammerMaterials.Get( from ) : null;
			var toMaterial = HammerMaterials.Get( to ) ?? S.Material.Load( HammerMaterials.DefaultKey );
			var list = targets.Where( t => t.Component != null ).ToList();
			if ( list.Count == 0 ) return 0;

			var changed = 0;
			using ( new MeshUndoScope( "Replace Material", list.Select( t => t.Component ) ) )
			{
				foreach ( var (c, faces) in list )
				{
					var hits = faces.Where( f => f.IsValid && (from == null ? c.Mesh.GetFaceMaterial( f ) == null || c.Mesh.GetFaceMaterial( f )?.Name == HammerMaterials.DefaultKey : c.Mesh.GetFaceMaterial( f )?.Name == fromKey?.Name) ).ToList();
					if ( hits.Count == 0 ) continue;
					c.Mesh.AssignMaterialToFaces( hits, toMaterial );
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters( hits );
					changed += hits.Count;
				}
			}

			HammerViews.RepaintAll();
			return changed;
		}

		// ─────────────────────────────── Meshes ───────────────────────────────

		Vector3? _objectPivot;
		int _objectPivotFor;
		int _pivotStep;

		/// <summary>
		/// Where rotate and scale turn the selected objects: a pivot set with the Pivot buttons,
		/// or the usual handle position. Forgotten when the selection changes.
		/// </summary>
		Vector3 ObjectPivot()
		{
			if ( _objectPivot.HasValue && _objectPivotFor == UnityEditor.Selection.activeInstanceID )
				return _objectPivot.Value;
			_objectPivot = null;

			// Unity's own handle position, worked out here: it's only kept up to date while a
			// scene view is drawing (with none open it's infinite)
			var transforms = UnityEditor.Selection.transforms;
			if ( transforms.Length == 0 ) return Vector3.zero;
			if ( Tools.pivotMode == PivotMode.Pivot && UnityEditor.Selection.activeTransform != null )
				return UnityEditor.Selection.activeTransform.position;

			var renderers = transforms.SelectMany( x => x.GetComponentsInChildren<Renderer>() ).ToList();
			if ( renderers.Count == 0 ) return transforms[0].position;
			var b = renderers[0].bounds;
			foreach ( var r in renderers ) b.Encapsulate( r.bounds );
			return b.center;
		}

		void SetObjectPivot( Vector3? pivot )
		{
			_objectPivot = pivot;
			_objectPivotFor = UnityEditor.Selection.activeInstanceID;
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Pivot Next / Previous: step the pivot through the selection's middle, the middle of
		/// its bottom and top, and its eight corners.
		/// </summary>
		public void StepPivot( int direction )
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 ) return;
			var b = MeshBounds( meshes );

			var points = new List<Vector3> { b.center, new( b.center.x, b.min.y, b.center.z ), new( b.center.x, b.max.y, b.center.z ) };
			for ( int i = 0; i < 8; i++ )
				points.Add( new Vector3( (i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z ) );

			_pivotStep = ((_pivotStep + direction) % points.Count + points.Count) % points.Count;
			SetObjectPivot( points[_pivotStep] );
		}

		public void ClearObjectPivot() { _pivotStep = 0; SetObjectPivot( null ); }
		public void PivotToWorldOrigin() => SetObjectPivot( Vector3.zero );

		/// <summary>
		/// Pivot to the middle of the view: the surface at the centre of the 3D view, or the ground.
		/// </summary>
		public void PivotToViewCenter()
		{
			var camera = (_lastMouseView ?? _view)?.Camera;
			if ( camera == null ) return;

			var ray = new Ray( camera.transform.position, camera.transform.forward );
			if ( MeshPicking.RaycastFace( ray, Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ), out var hit ) )
				SetObjectPivot( hit.Point );
			else if ( new Plane( Vector3.up, Vector3.zero ).Raycast( ray, out var t ) )
				SetObjectPivot( ray.GetPoint( t ) );
			else
				SetObjectPivot( camera.transform.position + camera.transform.forward * 5 );
		}

		/// <summary>
		/// Set Origin To Pivot (Ctrl+D): move each selected object's origin to the pivot, without
		/// moving its geometry.
		/// </summary>
		public void SetOriginToPivot()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 ) return;
			var pivot = ObjectPivot();

			Undo.RecordObjects( meshes.SelectMany( c => new Object[] { c, c.transform } ).ToArray(), "Set Origin To Pivot" );
			foreach ( var c in meshes )
			{
				MoveOrigin( c, pivot );
				c.Commit();
				EditorUtility.SetDirty( c );
			}
			Done();
		}

		/// <summary>
		/// Freeze Transform: build each object's rotation and scale into its mesh and reset them,
		/// so it looks the same but is axis aligned and unscaled.
		/// </summary>
		public void FreezeTransform()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 ) return;

			Undo.RecordObjects( meshes.SelectMany( c => new Object[] { c, c.transform } ).ToArray(), "Freeze Transform" );
			foreach ( var c in meshes )
			{
				var mesh = c.Mesh;
				var world = mesh.VertexHandles.Select( v => (v, c.SourceToWorld( mesh.GetVertexPosition( v ) )) ).ToList();
				c.transform.rotation = Quaternion.identity;
				c.transform.localScale = Vector3.one;
				mesh.SetTransform( c.WorldTransform );

				// Winding flips if the scale was mirrored
				foreach ( var (v, p) in world ) mesh.SetVertexPosition( v, c.WorldToSource( p ) );
				mesh.ComputeFaceTextureCoordinatesFromParameters();
				c.Commit();
				EditorUtility.SetDirty( c );
			}
			Done();
		}

		/// <summary>
		/// Clear Rotation and Scale (Ctrl+Num0).
		/// </summary>
		public void ClearRotationAndScale()
		{
			var transforms = UnityEditor.Selection.transforms;
			if ( transforms.Length == 0 ) return;
			Undo.RecordObjects( transforms, "Clear Rotation and Scale" );
			foreach ( var t in transforms )
			{
				t.rotation = Quaternion.identity;
				t.localScale = Vector3.one;
			}
			Done();
		}

		/// <summary>
		/// Merge Meshes by Edge: join the selected objects and weld their vertices where they meet.
		/// </summary>
		public void MergeMeshesByEdge()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count < 2 ) return;
			var target = UnityEditor.Selection.activeGameObject?.GetComponent<HammerMesh>() ?? meshes[0];

			MergeMeshes();
			using ( Scope( "Weld Merged Edges", new[] { target } ) )
				target.Mesh.WeldCoincidentVertices( 0.01f );
			Done();
		}

		/// <summary>
		/// The surface under the mouse in the view it was last over, not counting the selection.
		/// </summary>
		bool SurfaceUnderMouse( ICollection<HammerMesh> ignore, out MeshPicking.FaceHit hit )
		{
			hit = default;
			if ( _lastMouseView == null ) return false;

			var previous = HammerViews.Current;
			HammerViews.Current = _lastMouseView;
			try
			{
				var others = Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Where( c => c.isActiveAndEnabled && !ignore.Contains( c ) );
				return MeshPicking.RaycastFace( HammerGUI.GUIToRay( _lastMouse ), others, out hit );
			}
			finally
			{
				HammerViews.Current = previous;
			}
		}

		/// <summary>
		/// Align to Target (Alt+T): stand the selection on the surface under the cursor (turned
		/// to the surface too, with Align To Surface on).
		/// </summary>
		public void AlignToTarget()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 || !SurfaceUnderMouse( meshes, out var hit ) ) return;

			var transforms = meshes.Select( c => c.transform ).ToArray();
			Undo.RecordObjects( transforms, "Align to Target" );

			if ( HammerSettings.AlignToSurface )
				TurnToNormal( transforms, hit.Normal );

			var b = MeshBounds( meshes );
			var n = hit.Normal.normalized;
			var extent = Mathf.Abs( b.extents.x * n.x ) + Mathf.Abs( b.extents.y * n.y ) + Mathf.Abs( b.extents.z * n.z );
			var delta = hit.Point + n * extent - b.center;
			foreach ( var t in transforms ) t.position += delta;
			Done();
		}

		/// <summary>
		/// Rotate to Target (Alt+R): turn the selection so its up matches the surface under the cursor.
		/// </summary>
		public void RotateToTarget()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 || !SurfaceUnderMouse( meshes, out var hit ) ) return;

			var transforms = meshes.Select( c => c.transform ).ToArray();
			Undo.RecordObjects( transforms, "Rotate to Target" );
			TurnToNormal( transforms, hit.Normal );
			Done();
		}

		static void TurnToNormal( Transform[] transforms, Vector3 normal )
		{
			foreach ( var t in transforms )
			{
				var turn = Quaternion.FromToRotation( t.up, normal.normalized );
				t.rotation = turn * t.rotation;
			}
		}
	}
}
