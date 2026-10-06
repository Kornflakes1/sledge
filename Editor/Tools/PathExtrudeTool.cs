using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Alt+X. Sweep a profile (open edges on one mesh) along a path (edges on another mesh) into
	/// a new mesh. Port of the s&amp;box path extrude tool.
	/// </summary>
	public sealed class PathExtrudeTool : SubTool
	{
		public enum Origin
		{
			/// <summary>The profile keeps its world position, offset only by rotation onto the path.</summary>
			Absolute,

			/// <summary>The profile is centred on the path start using its object's origin.</summary>
			ObjectLocal,
		}

		static Origin _origin = Origin.Absolute;
		static bool _deleteSource;

		List<IGrouping<HammerMesh, MeshEdge>> _groups;
		List<MeshEdge> _edges;
		GameObject _preview;

		public override string Title => "Path Extrude";
		public override string Help => "Select the profile edges (open) on one mesh and the path edges on another. Enter creates the mesh, Esc cancels.";

		public static bool CanExtrude( List<IGrouping<HammerMesh, MeshEdge>> groups, List<MeshEdge> edges )
		{
			if ( groups.Count != 2 ) return false;
			if ( edges.Any( x => !x.IsValid ) ) return false;
			return groups.Any( g => g.All( x => x.IsOpen ) );
		}

		public static void Open( HammerMeshTool tool )
		{
			var edges = tool.SelectedEdges.ToList();
			var groups = edges.GroupBy( x => x.Component ).ToList();

			if ( !CanExtrude( groups, edges ) )
			{
				Debug.LogWarning( "Path Extrude needs open profile edges on one mesh and path edges on another." );
				return;
			}

			var extrude = new PathExtrudeTool { _groups = groups, _edges = edges };
			tool.BeginSubTool( extrude );
			extrude.UpdatePreview();
		}

		public override void OnOverlayGUI()
		{
			EditorGUI.BeginChangeCheck();
			_origin = (Origin)EditorGUILayout.EnumPopup( "Profile Origin", _origin );
			_deleteSource = EditorGUILayout.Toggle( "Delete Profile Object", _deleteSource );
			if ( EditorGUI.EndChangeCheck() )
				UpdatePreview();
		}

		protected override void OnSettingsUndone() => UpdatePreview();

		void UpdatePreview()
		{
			if ( !TryBuildMesh( out var mesh, out var profile ) )
			{
				DestroyPreview();
				return;
			}

			if ( _preview == null )
			{
				_preview = new GameObject( "Path Extrude Preview" ) { hideFlags = HideFlags.HideAndDontSave };
				_preview.transform.SetParent( profile.transform.parent, false );
				_preview.AddComponent<HammerMesh>().SmoothingAngle = 40;
			}

			_preview.transform.SetPositionAndRotation( profile.transform.position, profile.transform.rotation );
			_preview.transform.localScale = profile.transform.localScale;
			_preview.GetComponent<HammerMesh>().SetPreviewMesh( mesh );
			HammerViews.RepaintAll();
		}

		void DestroyPreview()
		{
			if ( _preview != null )
				UnityEngine.Object.DestroyImmediate( _preview );
			_preview = null;
		}

		bool TryBuildMesh( out S.PolygonMesh result, out HammerMesh profileComponent )
		{
			result = null;
			profileComponent = null;

			if ( !CanExtrude( _groups, _edges ) )
				return false;

			var openGroups = _groups.Where( g => g.All( x => x.IsOpen ) ).ToList();
			// If both are wires, selection order decides: profile first, then path
			var profileGroup = openGroups.Count == 1 ? openGroups[0] : _groups[0];
			var pathGroup = _groups.First( g => g != profileGroup );

			var component = profileGroup.Key;
			var mesh = component.Mesh;
			var transform = component.WorldTransform;

			// Dedupe opposite half-edges, winding away from the bordering face if there is one
			var profileEdges = profileGroup
				.GroupBy( e => Math.Min( e.Handle.Index, mesh.GetOppositeHalfEdge( e.Handle ).Index ) )
				.Select( g => g.First() )
				.Select( e =>
				{
					mesh.GetFacesConnectedToEdge( e.Handle, out var fa, out var fb );
					var h = fa != FaceHandle.Invalid ? mesh.GetOppositeHalfEdge( e.Handle ) : e.Handle;
					mesh.GetEdgeVertices( h, out var a, out var b );
					return (A: a, B: b, SourceFace: fa != FaceHandle.Invalid ? fa : fb);
				} )
				.ToList();

			if ( profileEdges.All( x => x.SourceFace == FaceHandle.Invalid ) )
			{
				var remaining = profileEdges.ToList();
				profileEdges = new List<(VertexHandle A, VertexHandle B, FaceHandle SourceFace)> { remaining[0] };
				remaining.RemoveAt( 0 );

				while ( remaining.Count > 0 )
				{
					var tail = profileEdges[^1].B;
					var idx = remaining.FindIndex( x => x.A == tail || x.B == tail );
					if ( idx < 0 ) break;
					var next = remaining[idx];
					remaining.RemoveAt( idx );
					profileEdges.Add( next.A == tail ? next : (next.B, next.A, next.SourceFace) );
				}

				profileEdges.AddRange( remaining );
			}

			S.Vector3 ProfilePos( VertexHandle v ) => transform.PointToWorld( mesh.GetVertexPosition( v ) );

			var chainCenter = profileEdges.Aggregate( S.Vector3.Zero, ( acc, e ) => acc + ProfilePos( e.A ) + ProfilePos( e.B ) ) / (profileEdges.Count * 2);
			var profileCenter = _origin == Origin.ObjectLocal ? transform.Position : chainCenter;

			// Order the path edges into a polyline
			var pathMesh = pathGroup.Key.Mesh;
			var pathTransform = pathGroup.Key.WorldTransform;
			var segments = pathGroup.Select( e =>
			{
				pathMesh.GetEdgeVertices( e.Handle, out var a, out var b );
				return a.Index < b.Index ? (A: a, B: b) : (A: b, B: a);
			} ).Distinct().ToList();

			S.Vector3 PathPos( VertexHandle v ) => pathTransform.PointToWorld( pathMesh.GetVertexPosition( v ) );

			var ends = segments.SelectMany( s => new[] { s.A, s.B } ).ToList();
			var candidates = ends.GroupBy( v => v ).Where( g => g.Count() == 1 ).Select( g => g.Key ).ToList();
			if ( candidates.Count == 0 ) candidates = ends.Distinct().ToList();

			var start = candidates.OrderBy( v => PathPos( v ).DistanceSquared( chainCenter ) ).First();
			var pathVertices = new List<VertexHandle> { start };

			while ( segments.Count > 0 )
			{
				var index = segments.FindIndex( s => s.A == pathVertices[^1] || s.B == pathVertices[^1] );
				if ( index < 0 ) return false;
				pathVertices.Add( segments[index].A == pathVertices[^1] ? segments[index].B : segments[index].A );
				segments.RemoveAt( index );
			}

			var closed = pathVertices.Count > 2 && pathVertices[0] == pathVertices[^1];
			var points = pathVertices.Select( PathPos ).ToList();
			if ( points.Count < 2 ) return false;

			// Best fit plane normal of the profile chain
			var profileNormal = S.Vector3.Zero;
			foreach ( var (a, b, _) in profileEdges )
			{
				var cross = S.Vector3.Cross( ProfilePos( a ) - chainCenter, ProfilePos( b ) - chainCenter );
				profileNormal += S.Vector3.Dot( cross, profileNormal ) < 0 ? -cross : cross;
			}

			profileNormal = profileNormal.Normal;
			if ( S.Vector3.Dot( profileNormal, points[1] - points[0] ) < 0 )
				profileNormal = -profileNormal;

			S.Vector3 Tangent( int i ) => i < points.Count - 1
				? (points[i + 1] - points[i]).Normal
				: closed ? (points[1] - points[0]).Normal : (points[i] - points[i - 1]).Normal;

			S.Vector3 InTangent( int i ) => i > 0
				? (points[i] - points[i - 1]).Normal
				: closed ? (points[0] - points[^2]).Normal : Tangent( 0 );

			// Double reflection transport: rotation minimising, so the profile doesn't twist
			S.Vector3 Sweep( S.Vector3 point, int i )
			{
				static S.Vector3 Reflect( S.Vector3 v, S.Vector3 axis ) => v - axis * (2.0f / S.Vector3.Dot( axis, axis ) * S.Vector3.Dot( axis, v ));

				var segment = points[i] - points[i - 1];
				var reflected = Reflect( point - points[i - 1], segment );
				var reflectedTangent = Reflect( Tangent( i - 1 ), segment );
				var correction = Tangent( i ) - reflectedTangent;
				if ( S.Vector3.Dot( correction, correction ) > 1e-8f )
					reflected = Reflect( reflected, correction );
				return points[i] + reflected;
			}

			// Shear a ring point onto the corner's bisector plane, keeping the strip width
			S.Vector3 Mitre( S.Vector3 point, int i )
			{
				var dir = Tangent( i );
				var normal = (InTangent( i ) + dir).Normal;
				if ( normal.IsNearZeroLength ) normal = dir;
				return point + dir * S.Vector3.Dot( points[i] - point, normal ) / S.Vector3.Dot( dir, normal );
			}

			var newMesh = new S.PolygonMesh();
			newMesh.SetTransform( mesh.Transform );
			var ringVerts = new Dictionary<(int, VertexHandle), VertexHandle>();
			var ringPositions = new Dictionary<(int, VertexHandle), S.Vector3>();

			VertexHandle RingVertex( int i, VertexHandle v )
			{
				if ( ringVerts.TryGetValue( (i, v), out var existing ) )
					return existing;

				S.Vector3 position;
				if ( i == 0 )
				{
					position = ProfilePos( v );
					if ( _origin == Origin.ObjectLocal )
						position += points[0] - profileCenter;

					var dir = Tangent( 0 );
					position = profileNormal.IsNearZeroLength
						? position + dir * S.Vector3.Dot( points[0] - position, dir )
						: points[0] + S.Rotation.FromToRotation( profileNormal, dir ) * (position - points[0]);
				}
				else if ( closed && i == points.Count - 1 )
				{
					position = ringPositions[(0, v)];
				}
				else
				{
					position = Sweep( ringPositions[(i - 1, v)], i );
				}

				ringPositions[(i, v)] = position;
				return ringVerts[(i, v)] = newMesh.AddVertex( transform.PointToLocal( Mitre( position, i ) ) );
			}

			var added = 0;
			foreach ( var (a, b, sourceFace) in profileEdges )
			{
				for ( var i = 1; i < points.Count; i++ )
				{
					var face = newMesh.AddFace( RingVertex( i - 1, a ), RingVertex( i - 1, b ), RingVertex( i, b ), RingVertex( i, a ) );
					if ( !face.IsValid ) continue;

					added++;
					if ( sourceFace != FaceHandle.Invalid )
						newMesh.SetFaceMaterial( face, mesh.GetFaceMaterial( sourceFace ) );
					newMesh.TextureAlignToGrid( newMesh.Transform, face );
				}
			}

			if ( added == 0 ) return false;

			if ( closed )
			{
				var seam = ringVerts.Where( x => x.Key.Item1 == 0 || x.Key.Item1 == points.Count - 1 ).Select( x => x.Value ).ToList();
				newMesh.MergeVerticesWithinDistance( seam, 0.01f, false, false, out _ );
			}

			result = newMesh;
			profileComponent = component;
			return true;
		}

		public override void OnViewGUI( HammerView view )
		{
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( Event.current.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );
		}

		public override void Apply()
		{
			DestroyPreview();

			if ( !TryBuildMesh( out var mesh, out var profile ) )
			{
				Close();
				return;
			}

			var go = new GameObject( profile.name );
			Undo.RegisterCreatedObjectUndo( go, "Path Extrude" );
			go.transform.SetParent( profile.transform.parent, false );
			go.transform.SetPositionAndRotation( profile.transform.position, profile.transform.rotation );
			go.transform.localScale = profile.transform.localScale;
			go.isStatic = profile.gameObject.isStatic;

			var c = go.AddComponent<HammerMesh>();
			c.SmoothingAngle = 40;
			c.Mesh = mesh;

			if ( _deleteSource )
				Undo.DestroyObjectImmediate( profile.gameObject );

			Tool.Selection.Clear();
			UnityEditor.Selection.activeGameObject = go;
			Close();
		}

		public override void Cancel()
		{
			DestroyPreview();
			Close();
		}
	}
}
