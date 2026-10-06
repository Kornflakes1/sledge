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
	/// Alt+B. Bridge two open edge loops (or two face regions, which are removed first) with a
	/// curved, segmented tube. Drag the two control points to shape the curve. Port of the s&amp;box
	/// bridge tool.
	/// </summary>
	public sealed class BridgeTool : SubTool
	{
		struct Basis
		{
			public S.Vector3 Position;
			public S.Vector3 Normal;
			public S.Vector3 Tangent;
			public bool HasPlane;
		}

		static int _steps = 4;
		static int _twist;
		static S.PolygonMesh.BridgeUVMode _uvMode = S.PolygonMesh.BridgeUVMode.Auto;
		static float _repeatsU = 1;
		static float _repeatsV = 1;

		HammerMesh _meshA;
		HammerMesh _meshB;
		S.PolygonMesh _originalA;
		S.PolygonMesh _originalB;
		S.PolygonMesh _editMesh;
		List<HalfEdgeHandle> _fromEdges;
		List<HalfEdgeHandle> _toEdges;
		List<HalfEdgeHandle> _createdEdges;
		bool _faces;

		readonly Basis[] _basis = new Basis[2];
		readonly S.Vector3[] _controlPoints = new S.Vector3[2];

		public override string Title => "Bridge";
		public override string Help => "Drag the control points to curve the bridge. [ ] change steps. Enter applies, Esc cancels.";

		public static void Open( HammerMeshTool tool )
		{
			var bridge = new BridgeTool();
			bool ok = tool.Mode == EditMode.Face
				? bridge.SetupFaces( tool.SelectedFaces.ToArray() )
				: bridge.SetupEdges( tool.SelectedEdges.ToArray() );

			if ( !ok )
			{
				bridge.Restore();
				Debug.LogWarning( "Bridge needs two open edge loops (or two face regions) with the same number of edges." );
				return;
			}

			tool.BeginSubTool( bridge );
		}

		bool SetupFaces( MeshFace[] faces )
		{
			_faces = true;
			var groups = faces.GroupBy( x => x.Component ).ToList();
			if ( groups.Count is 0 or > 2 ) return false;

			var edit = new S.PolygonMesh();
			var a = groups[0].Key;
			edit.SetTransform( a.Mesh.Transform );
			edit.MergeMesh( a.Mesh, S.Transform.Zero, out _, out _, out var remap );
			var handles = groups[0].Select( x => remap[x.Handle] ).ToList();
			_meshA = a;
			_originalA = a.Mesh;

			if ( groups.Count == 2 )
			{
				var b = groups[1].Key;
				edit.MergeMesh( b.Mesh, a.WorldTransform.ToLocal( b.WorldTransform ), out _, out _, out remap );
				handles.AddRange( groups[1].Select( x => remap[x.Handle] ) );
				_meshB = b;
				_originalB = b.Mesh;
			}

			edit.FindBoundaryEdgesConnectedToFaces( handles, out var boundary );
			edit.FindClosedFaces( handles, out var closed );
			edit.RemoveFaces( closed );

			return Finish( edit, boundary );
		}

		bool SetupEdges( MeshEdge[] edges )
		{
			var groups = edges.GroupBy( x => x.Component ).ToList();
			if ( groups.Count is 0 or > 2 ) return false;

			var edit = new S.PolygonMesh();
			var a = groups[0].Key;
			edit.SetTransform( a.Mesh.Transform );
			edit.MergeMesh( a.Mesh, S.Transform.Zero, out _, out var remap, out _ );
			var all = groups[0].Select( x => remap[x.Handle] ).ToList();
			_meshA = a;
			_originalA = a.Mesh;

			if ( groups.Count == 2 )
			{
				var b = groups[1].Key;
				edit.MergeMesh( b.Mesh, a.WorldTransform.ToLocal( b.WorldTransform ), out _, out remap, out _ );
				all.AddRange( groups[1].Select( x => remap[x.Handle] ) );
				_meshB = b;
				_originalB = b.Mesh;
			}

			return Finish( edit, all );
		}

		bool Finish( S.PolygonMesh edit, List<HalfEdgeHandle> edges )
		{
			edit.FindOpenEdgeIslands( edges, out var islands );
			if ( islands.Count != 2 || islands[0].Count != islands[1].Count )
				return false;

			if ( !edit.CorrelateOpenEdges( islands[0], islands[1], out _fromEdges, out _toEdges ) )
				return false;

			_editMesh = edit;
			_meshA.SetPreviewMesh( edit );
			_meshB?.SetPreviewMesh( new S.PolygonMesh() );

			ComputeBasis();
			ComputeDefaultControlPoints();
			UpdateBridge();
			return true;
		}

		public override bool OnBracket( int direction )
		{
			_steps = Math.Clamp( _steps + direction, 1, 128 );
			UpdateBridge();
			return true;
		}

		public override void OnOverlayGUI()
		{
			EditorGUI.BeginChangeCheck();
			_steps = EditorGUILayout.IntSlider( "Steps", _steps, 1, 64 );
			_twist = EditorGUILayout.IntSlider( "Twist", _twist, -16, 16 );
			_uvMode = (S.PolygonMesh.BridgeUVMode)EditorGUILayout.EnumPopup( "UV Mode", _uvMode );
			_repeatsU = EditorGUILayout.FloatField( "Repeats U", _repeatsU );
			_repeatsV = EditorGUILayout.FloatField( "Repeats V", _repeatsV );
			if ( EditorGUI.EndChangeCheck() )
				UpdateBridge();

			if ( GUILayout.Button( "Reset Curve", EditorStyles.miniButton ) )
			{
				ComputeDefaultControlPoints();
				UpdateBridge();
			}
		}

		protected override void OnSettingsUndone() => UpdateBridge();

		void UpdateBridge()
		{
			if ( _editMesh is null ) return;

			var mesh = new S.PolygonMesh();
			mesh.SetTransform( _editMesh.Transform );
			mesh.MergeMesh( _editMesh, S.Transform.Zero, out _, out var remap, out _ );

			var from = _fromEdges.Select( x => remap[x] ).ToList();
			var to = _toEdges.Select( x => remap[x] ).ToList();
			var count = to.Count;

			var twist = _twist;
			if ( twist < 0 ) twist = count - (-twist % count);
			var twisted = Enumerable.Range( 0, count ).Select( i => to[(i + twist) % count] ).ToList();

			var fromBasis = _basis[0];
			var toBasis = _basis[1];
			var distance = (fromBasis.Position - toBasis.Position).Length;
			if ( distance <= 0 ) return;

			var fromDelta = (_controlPoints[0] - fromBasis.Position) / distance;
			var toDelta = (_controlPoints[1] - toBasis.Position) / distance;

			var parameters = new S.PolygonMesh.BridgeInterpolationParameters
			{
				NumSteps = _steps,
				FromDeltaN = S.Vector3.Dot( fromDelta, fromBasis.Normal ),
				FromDeltaT = fromBasis.HasPlane ? S.Vector3.Dot( fromDelta, fromBasis.Tangent ) : 0,
				ToDeltaN = S.Vector3.Dot( toDelta, toBasis.Normal ),
				ToDeltaT = toBasis.HasPlane ? S.Vector3.Dot( toDelta, toBasis.Tangent ) : 0,
				RepeatsU = _repeatsU,
				RepeatsV = _repeatsV,
				UVMode = _uvMode,
			};

			if ( mesh.BridgeEdgesInterpolated( from, twisted, parameters, out _createdEdges ) )
				_createdEdges.AddRange( to );

			_meshA.SetPreviewMesh( mesh );
			HammerViews.RepaintAll();
		}

		S.Vector3 EdgeListCenter( List<HalfEdgeHandle> edges )
		{
			var transform = _meshA.WorldTransform;
			var mesh = _editMesh;

			if ( mesh.ClassifyEdgeListConnectivity( edges ) == ComponentConnectivityType.Loop )
			{
				var sum = S.Vector3.Zero;
				var n = 0;
				foreach ( var e in edges )
				{
					if ( !e.IsValid ) continue;
					mesh.GetEdgeVertexPositions( e, transform, out var a, out var b );
					sum += (a + b) * 0.5f;
					n++;
				}
				return n > 0 ? sum / n : S.Vector3.Zero;
			}

			mesh.GetEdgeVertexPositions( edges[edges.Count / 2], transform, out var a0, out var a1 );
			mesh.GetEdgeVertexPositions( edges[(edges.Count - 1) / 2], transform, out var b0, out var b1 );
			return (a0 + a1 + b0 + b1) * 0.25f;
		}

		S.Vector3 EdgeListNormal( List<HalfEdgeHandle> edges )
		{
			var transform = _meshA.WorldTransform;
			var mesh = _editMesh;

			if ( mesh.ClassifyEdgeListConnectivity( edges ) == ComponentConnectivityType.Loop )
			{
				mesh.ComputeNormalForOpenEdgeLoop( edges, transform, out var normal, out _ );
				return normal;
			}

			return transform.NormalToWorld( mesh.ComputeOpenEdgeExtendDirection( edges[edges.Count / 2] ).Normal );
		}

		void ComputeBasis()
		{
			for ( int i = 0; i < 2; i++ )
			{
				var edges = i == 0 ? _fromEdges : _toEdges;
				var other = i == 0 ? _toEdges : _fromEdges;
				var position = EdgeListCenter( edges );
				var target = EdgeListCenter( other );
				var normal = EdgeListNormal( edges );

				var basis = new Basis { Position = position, Normal = normal };
				var toTarget = (target - position).Normal;
				var facing = S.Vector3.Dot( target - position, normal ) >= 0 ? normal : -normal;

				if ( MathF.Abs( S.Vector3.Dot( toTarget, facing ) ) <= 0.999f )
				{
					var binormal = S.Vector3.Cross( toTarget, facing ).Normal;
					basis.Tangent = S.Vector3.Cross( binormal, facing ).Normal;
					basis.HasPlane = true;
				}

				_basis[i] = basis;
			}
		}

		void ComputeDefaultControlPoints()
		{
			var from = _basis[0];
			var to = _basis[1];
			var fromNormal = from.Normal;
			var toNormal = to.Normal;

			if ( S.Vector3.Dot( fromNormal, to.Position - from.Position ) <= -0.01f ) fromNormal = -fromNormal;
			if ( S.Vector3.Dot( toNormal, from.Position - to.Position ) <= -0.01f ) toNormal = -toNormal;

			// Quarter circle bezier handles
			const float sqrt2 = 1.41421356f;
			const float kappa = 4.0f * (sqrt2 - 1.0f) / 3.0f;
			var radius = (from.Position - to.Position).Length / sqrt2;

			_controlPoints[0] = from.Position + fromNormal * radius * kappa;
			_controlPoints[1] = to.Position + toNormal * radius * kappa;
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			for ( int i = 0; i < 2; i++ )
			{
				var basis = _basis[i];
				var origin = SourceSpace.ToUnityPosition( basis.Position );
				var point = SourceSpace.ToUnityPosition( _controlPoints[i] );
				var normal = SourceSpace.ToUnityDirection( basis.Normal ).normalized;

				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
				Handles.color = new Color( 0.5f, 1, 1 );
				Handles.DrawLine( origin, point );

				EditorGUI.BeginChangeCheck();
				var size = HammerGUI.HandleSize( point ) * 0.08f;
				var moved = HammerGizmos.Slider( point, normal, size, Handles.SphereHandleCap );
				if ( EditorGUI.EndChangeCheck() )
				{
					var source = SourceSpace.ToSourcePosition( moved );
					var along = S.Vector3.Dot( source - basis.Position, basis.Normal );
					if ( HammerSettings.GridSnap ^ (e.control || e.command) )
						along = MathF.Round( along / HammerSettings.GridSize ) * HammerSettings.GridSize;

					_controlPoints[i] = basis.Position + basis.Normal * along;

					// Shift moves both ends together
					if ( e.shift )
						_controlPoints[1 - i] = _basis[1 - i].Position + _basis[1 - i].Normal * along;

					UpdateBridge();
				}
			}

			if ( e.type == EventType.Repaint && _createdEdges != null )
			{
				Handles.color = new Color( 0.31f, 0.78f, 1, 0.8f );
				var mesh = _meshA.Mesh;
				foreach ( var h in _createdEdges )
				{
					if ( !h.IsValid ) continue;
					var line = mesh.GetEdgeLine( h );
					Handles.DrawAAPolyLine( 2.0f, _meshA.SourceToWorld( line.Start ), _meshA.SourceToWorld( line.End ) );
				}
			}
		}

		void Restore()
		{
			if ( _meshA != null && _originalA != null ) _meshA.SetPreviewMesh( _originalA );
			if ( _meshB != null && _originalB != null ) _meshB.SetPreviewMesh( _originalB );
		}

		public override void Apply()
		{
			if ( _editMesh is null || _meshA == null )
			{
				Cancel();
				return;
			}

			var result = _meshA.Mesh;
			Restore();

			Undo.RecordObject( _meshA, "Bridge" );
			_meshA.SetPreviewMesh( result );
			_meshA.Commit();
			EditorUtility.SetDirty( _meshA );

			if ( _meshB != null )
				Undo.DestroyObjectImmediate( _meshB.gameObject );

			Tool.Selection.Clear();
			if ( _createdEdges != null )
			{
				foreach ( var h in _createdEdges )
				{
					if ( !h.IsValid ) continue;

					if ( _faces )
					{
						result.GetFacesConnectedToEdge( h, out var fa, out var fb );
						if ( fa.IsValid ) Tool.Selection.Add( new MeshFace( _meshA, fa ) );
						if ( fb.IsValid ) Tool.Selection.Add( new MeshFace( _meshA, fb ) );
					}
					else
					{
						Tool.Selection.Add( new MeshEdge( _meshA, h ) );
					}
				}
			}

			Close();
		}

		public override void Cancel()
		{
			Restore();
			Close();
		}
	}
}
